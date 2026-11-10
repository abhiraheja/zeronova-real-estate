using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Application.Features.Requirements;

/// <summary>
/// Closing a requirement (C3): the reason code beside the free-text note, the day it closed, and — closed (or lost) as
/// not available — the watch (D33): status <see cref="RequirementStatus.Watching"/> until the dealer's watch days. Reopening
/// clears it all. A close without a code is <see cref="RequirementCloseReason.Other"/> (a lost one keeps none).
/// </summary>
public static class RequirementClosing
{
    public static void Apply(RequirementDocument r, RequirementStatus status, RequirementCloseReason? code, string? note, EstateSettingsDocument s,
        DateTime now)
    {
        if (status is RequirementStatus.Active or RequirementStatus.OnHold)
        {
            r.Status = status;
            r.CloseReason = null;
            r.CloseReasonCode = null;
            r.ClosedAt = null;
            r.WatchUntil = null;
            return;
        }

        code ??= status switch
        {
            RequirementStatus.Watching => RequirementCloseReason.NotAvailable,
            RequirementStatus.Closed => RequirementCloseReason.Other,
            _ => null,
        };
        r.CloseReasonCode = code;
        r.CloseReason = EstateText.Clean(note);
        r.ClosedAt = now;
        if (code == RequirementCloseReason.NotAvailable)
        {
            r.Status = RequirementStatus.Watching;
            r.WatchUntil = now.AddDays(Math.Clamp(s.WatchDays, 1, 365));
        }
        else
        {
            r.Status = status == RequirementStatus.Watching ? RequirementStatus.Closed : status;
            r.WatchUntil = null;
        }
    }

    /// <summary>Matched both ways (D10, D33): active, or watched.</summary>
    public static bool IsMatchable(RequirementStatus status) => status is RequirementStatus.Active or RequirementStatus.Watching;
}

/// <summary>What a piece of unmet demand asks for — a requirement's criteria, or what an enquiry closed as not available asked for.</summary>
public sealed record DemandCriteria(
    PropertyPurpose Purpose, IReadOnlyList<PropertyType> Types, string? City, IReadOnlyList<string> Localities, IReadOnlyList<int> Bhk, decimal? BudgetMin,
    decimal? BudgetMax)
{
    public static DemandCriteria Of(RequirementDocument r) => new(r.Purpose, r.Types, r.City, r.Localities, r.Bhk, r.BudgetMin, r.BudgetMax);
}

/// <summary>
/// Watched demand (D33): an enquiry closed as not available becomes a watched requirement of its lead (made from the
/// enquiry's listing, its form's extra fields and what was wanted), so a later fit revives it; and the daily sweep that
/// closes watches past their date.
/// </summary>
public sealed class WatchedDemand(
    IRequirementRepository requirements, IPropertyRepository properties, IProjectRepository projects, IUnitTypeRepository unitTypes,
    IEstateSettingsRepository settings, ILocalityRepository localities, MatchEngine engine, Customers.CustomerBook customers, TimeProvider clock,
    ILogger<WatchedDemand> logger)
{
    /// <summary>
    /// The enquiry was closed as not available: its watched requirement — made now when it has no requirement, or the one
    /// made from it before, watched again. An enquiry linked to a requirement of its own (added while working it) leaves that
    /// requirement alone: the agent closes it. None without a lead (a requirement belongs to a lead).
    /// </summary>
    public async Task<RequirementDocument?> WatchEnquiryAsync(EnquiryDocument e, string agentId, CancellationToken ct)
    {
        if (e.LeadId is not { } leadId)
            return null;
        RequirementDocument? requirement = null;
        if (e.RequirementId is { } linked)
        {
            requirement = await requirements.GetDocument(linked).FirstOrDefaultAsync(ct);
            // The enquiry's own requirement (added while working it), or ours reopened by the agent: theirs to close.
            if (requirement is not null && (requirement.EnquiryId != e.Id || requirement.Status is RequirementStatus.Active or RequirementStatus.OnHold))
                return null;
        }

        var s = await EstateSettings.LoadAsync(settings, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var isNew = requirement is null;
        if (requirement is null)
        {
            var c = await CriteriaAsync(e, s, ct);
            requirement = new RequirementDocument
            {
                Id = requirements.GetNewId, LeadId = leadId, ContactId = e.ContactId, LeadName = e.Name, LeadPhone = SafePhone(e.Phone),
                AgentId = e.AgentId ?? agentId, EnquiryId = e.Id, Purpose = c.Purpose, Types = [.. c.Types], City = c.City,
                Localities = [.. c.Localities], LocalityKeys = [.. c.Localities.Select(EstateText.Key)], Bhk = [.. c.Bhk], BudgetMin = c.BudgetMin,
                BudgetMax = c.BudgetMax,
            };
        }
        requirement.Notes = e.Wanted;
        RequirementClosing.Apply(requirement, RequirementStatus.Closed, RequirementCloseReason.NotAvailable, $"Enquiry {e.Code}: {e.Wanted}", s, now);
        if (isNew)
            await requirements.AddAsync(requirement, ct);
        else
            await requirements.UpdateAsync(requirement, ct);
        foreach (var locality in requirement.Localities)
            await localities.EnsureAsync(locality, EstateText.Key(locality), requirement.City, ct);
        e.RequirementId = requirement.Id;
        await engine.ForRequirementAsync(requirement, ct);
        return requirement;
    }

    /// <summary>
    /// What the enquiry asked for: its property (purpose, type, place, BHK, price as the budget) or project and unit type,
    /// then the form's extra fields (budget, BHK, purpose, type, locality, city), then the words of what was wanted
    /// ("3 bhk", "80 lakh", "rent", "plot").
    /// </summary>
    public async Task<DemandCriteria> CriteriaAsync(EnquiryDocument e, EstateSettingsDocument s, CancellationToken ct, string? companyId = null)
    {
        PropertyPurpose? purpose = null;
        var types = new List<PropertyType>();
        string? city = null;
        var places = new List<string>();
        var bhk = new List<int>();
        decimal? min = null, max = null;

        if (e.PropertyId is { } propertyId && await properties.GetDocument(propertyId, true, companyId).FirstOrDefaultAsync(ct) is { } p)
        {
            purpose = p.Purpose;
            types.Add(p.Type);
            city = p.City;
            if (p.ProjectId is null && EstateText.Clean(p.Locality) is { } l)
                places.Add(l);
            if (p.Bhk is { } b)
                bhk.Add(b);
            max = p.PriceValue;
        }
        var projectId = e.ProjectId;
        if (projectId is not null && await projects.GetDocument(projectId, true, companyId).FirstOrDefaultAsync(ct) is { } project)
        {
            purpose ??= PropertyPurpose.Sale;
            city ??= project.City;
            if (EstateText.Clean(project.Locality) is { } l && !places.Contains(l))
                places.Add(l);
        }
        if (e.UnitTypeId is { } typeId && await unitTypes.GetDocument(typeId, true, companyId).FirstOrDefaultAsync(ct) is { } type)
        {
            if (!types.Contains(type.Type))
                types.Add(type.Type);
            if (type.Bhk is { } b && !bhk.Contains(b))
                bhk.Add(b);
        }

        foreach (var field in e.Extra)
        {
            var key = field.Key.ToLowerInvariant();
            if (key.Contains("budget") || key.Contains("price"))
            {
                var amounts = DemandText.Amounts(field.Value, true);
                if (amounts.Count > 0)
                    (min, max) = amounts.Count > 1 ? (amounts.Min(), amounts.Max()) : (min, (decimal?)amounts[0]);
            }
            else if (key.Contains("bhk") || key.Contains("bedroom"))
                bhk.AddRange(DemandText.Bhk(field.Value, true).Where(b => !bhk.Contains(b)));
            else if (key.Contains("purpose") || key.Contains("looking"))
                purpose = DemandText.Purpose(field.Value) ?? purpose;
            else if (key.Contains("type"))
                types.AddRange(DemandText.Types(field.Value).Where(t => !types.Contains(t)));
            // "area" only as the whole key: plotArea, superArea … are sizes, not places.
            else if (key.Contains("locality") || key.Contains("location") || key.Contains("sector") || key == "area")
            {
                if (EstateText.Clean(field.Value) is { } l && !places.Contains(l, StringComparer.OrdinalIgnoreCase))
                    places.Add(l);
            }
            else if (key.Contains("city"))
                city = EstateText.Clean(field.Value) ?? city;
        }

        if (e.Wanted is { } wanted)
        {
            purpose ??= DemandText.Purpose(wanted);
            if (bhk.Count == 0)
                bhk.AddRange(DemandText.Bhk(wanted, false));
            if (types.Count == 0)
                types.AddRange(DemandText.Types(wanted));
            if (places.Count == 0 && DemandText.Sector(wanted) is { } sector)
                places.Add(sector);
            if (min is null && max is null && DemandText.Amounts(wanted) is { Count: > 0 } amounts)
                (min, max) = amounts.Count > 1 ? (amounts.Min(), amounts.Max()) : ((decimal?)null, amounts[0]);
        }

        return new DemandCriteria(purpose ?? PropertyPurpose.Sale, [.. types.Distinct()], city ?? s.DefaultCity, [.. places.Take(20)], [.. bhk.Distinct().Order()],
            min is > 0 ? min : null, max is > 0 ? max : null);
    }

    /// <summary>
    /// Closes the watches whose date passed (status Closed, the reason kept); answers how many were closed. A row that fails
    /// is logged and the sweep goes on with the rest (it is tried again on the next run); a batch where none could be closed
    /// answers 0, so the job's loop stops.
    /// </summary>
    public async Task<int> EndWatchesAsync(CancellationToken ct)
    {
        var ended = await requirements.WatchesEndedAsync(clock.GetUtcNow().UtcDateTime, 100, ct);
        var closed = 0;
        foreach (var requirement in ended)
        {
            var company = requirement.CompanyId;
            try
            {
                requirement.Status = RequirementStatus.Closed;
                await requirements.UpdateAsync(requirement, ct, company);
                closed++;
                await engine.ForRequirementAsync(requirement, ct, company);   // its still-new (revived) matches drop
                await customers.TouchAsync(requirement.ContactId, [requirement.LeadId], ct, company);
                logger.LogInformation("Watch on requirement {RequirementId} of company {CompanyId} ended.", requirement.Id, company);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ending the watch on requirement {RequirementId} of company {CompanyId} failed; the sweep goes on.", requirement.Id, company);
            }
        }
        return closed;
    }

    private static string? SafePhone(string? phone)
    {
        try
        {
            return EstateText.Phone(phone);
        }
        catch (Exceptions.ValidatorException)
        {
            return null;
        }
    }
}

/// <summary>Reading a need out of free text, as dealers and forms write it: "3 BHK", "80 lakh", "1.2 cr", "25k", "kothi on rent".</summary>
public static partial class DemandText
{
    /// <summary>
    /// Amounts in rupees: "80 lakh" / "80L" / "1.2 cr" / "25k" / "₹80,00,000" / "Rs 25000"; "50-80 lakh" is two. Never an
    /// amount: a number followed by an area unit ("1650 sq ft", "200 gaj", "5 marla", "2 kanal", "1 acre", "3 bigha"), a
    /// year (1990–2100) or a phone-like number (10 digits or more) without a unit or ₹. In free text a bare number (no ₹ / Rs
    /// / INR in front, no lakh / cr / k after) counts only from 1,00,000; in a budget field (<paramref name="moneyField"/>)
    /// from 1,000.
    /// </summary>
    public static IReadOnlyList<decimal> Amounts(string text, bool moneyField = false)
    {
        var lower = text.ToLowerInvariant();
        var raw = new List<(decimal Number, string Unit, bool Currency, int Digits, bool Area, int Start, int End)>();
        foreach (Match m in Amount().Matches(lower))
        {
            var digits = m.Groups[2].Value;
            if (!decimal.TryParse(digits.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
                continue;
            var unit = m.Groups[3].Value;
            raw.Add((n, unit, m.Groups[1].Success, digits.Count(char.IsDigit), unit.Length == 0 && AreaAfter().IsMatch(lower[(m.Index + m.Length)..]),
                m.Index, m.Index + m.Length));
        }
        // "50-80 lakh", "50 to 80 lakh": a bare number joined by a dash or "to" to one with a unit takes that unit; one joined
        // to an area ("1200-1500 sq ft") is an area too.
        for (var i = raw.Count - 2; i >= 0; i--)
        {
            if (raw[i].Unit.Length != 0 || lower[raw[i].End..raw[i + 1].Start].Trim() is not ("-" or "to" or "–"))
                continue;
            var next = raw[i + 1];
            if (next.Area)
                raw[i] = raw[i] with { Area = true };
            else if (next.Unit.Length > 0)
                raw[i] = raw[i] with { Unit = next.Unit, Currency = raw[i].Currency || next.Currency };
        }
        var found = new List<decimal>();
        foreach (var (n, unit, currency, digitCount, area, _, _) in raw)
        {
            if (area)
                continue;
            var value = unit switch
            {
                "cr" or "crore" or "crores" => n * 10_000_000m,
                "l" or "lac" or "lacs" or "lakh" or "lakhs" => n * 100_000m,
                "k" or "thousand" => n * 1_000m,
                _ => n,
            };
            if (unit.Length == 0 && !currency)
            {
                if (digitCount >= 10 || (n == Math.Floor(n) && n is >= 1990 and <= 2100) || value < (moneyField ? 1_000m : 100_000m))
                    continue;   // a phone number, a year, or a bare small number (a size, a floor, a count)
            }
            if (value < 10_000_000_000m)
                found.Add(Math.Round(value, 0));
        }
        return found;
    }

    /// <summary>An area unit right after a number: "sq ft", "sq. yd", "sqft", "sq m", "gaj", "marla", "kanal", "acre", "bigha".</summary>
    [GeneratedRegex(@"^\s*(?:sq\.?\s*(?:ft|feet|foot|yd|yds|yard|yards|m|mt|mtr|metre|meter|metres|meters)\b|sqft|sqyd|sqyds|sqm|sq\b|square\b|gaj|gaz|marla|marlas|kanal|kanals|acre|acres|bigha|bighas|biswa|hectare|hectares)",
        RegexOptions.CultureInvariant)]
    private static partial Regex AreaAfter();

    /// <summary>"3 BHK", "3bhk", "2/3 BHK"; <paramref name="bare"/> = the field is a BHK field, so "3" alone counts.</summary>
    public static IReadOnlyList<int> Bhk(string text, bool bare)
    {
        var list = Bhks().Matches(text).SelectMany(m => new[] { m.Groups[1].Value, m.Groups[2].Value }).Where(x => x.Length > 0).ToList();
        if (bare && list.Count == 0)
            list = [.. Digits().Matches(text).Select(m => m.Value)];
        return [.. list.Select(int.Parse).Where(b => b is >= 1 and <= 10).Distinct().Order()];
    }

    public static PropertyPurpose? Purpose(string text)
    {
        var t = text.ToLowerInvariant();
        if (Words(t, "rent", "rental", "kiraya", "lease", "tenant"))
            return PropertyPurpose.Rent;
        if (Words(t, "buy", "sale", "purchase", "kharidna", "resale"))
            return PropertyPurpose.Sale;
        return null;
    }

    private static readonly (string Word, PropertyType Type)[] TypeWords =
    [
        ("builder floor", PropertyType.BuilderFloor), ("independent floor", PropertyType.BuilderFloor), ("floor", PropertyType.BuilderFloor),
        ("flat", PropertyType.Flat), ("apartment", PropertyType.Flat), ("plot", PropertyType.Plot), ("kothi", PropertyType.House),
        ("house", PropertyType.House), ("villa", PropertyType.Villa), ("penthouse", PropertyType.Penthouse), ("shop", PropertyType.Shop),
        ("booth", PropertyType.Shop), ("showroom", PropertyType.Showroom), ("office", PropertyType.Office), ("warehouse", PropertyType.Warehouse),
        ("godown", PropertyType.Warehouse), ("farmhouse", PropertyType.Farmhouse), ("farm house", PropertyType.Farmhouse), ("pg", PropertyType.Pg),
        ("agricultural land", PropertyType.AgriculturalLand), ("industrial plot", PropertyType.IndustrialPlot),
    ];

    /// <summary>Property types named in the text, as whole words ("Flat", "BuilderFloor", "kothi", "builder floor").</summary>
    public static IReadOnlyList<PropertyType> Types(string text)
    {
        var t = text.ToLowerInvariant();
        var found = new List<PropertyType>();
        foreach (var type in Enum.GetValues<PropertyType>())
            if (Words(t, type.ToString().ToLowerInvariant()) && !found.Contains(type))
                found.Add(type);
        foreach (var (word, type) in TypeWords)
            if (Words(t, word) && !found.Contains(type))
                found.Add(type);
        // "builder floor" also says "floor"; "industrial plot" also "plot": the longer phrase wins.
        if (found.Contains(PropertyType.IndustrialPlot))
            found.Remove(PropertyType.Plot);
        return found;
    }

    /// <summary>"Sector 70" / "sec 70" / "phase 3b" in the text, as dealers name places, or null.</summary>
    public static string? Sector(string text) =>
        SectorWords().Match(text) is { Success: true } m
            ? (m.Groups[1].Value.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "Phase " : "Sector ") + m.Groups[2].Value.ToUpperInvariant()
            : null;

    [GeneratedRegex(@"\b(sector|sec|phase|ph)\.?\s*-?\s*(\d{1,3}[a-z]?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SectorWords();

    private static bool Words(string text, params string[] words) =>
        words.Any(w => Regex.IsMatch(text, $@"\b{Regex.Escape(w)}s?\b", RegexOptions.CultureInvariant));

    [GeneratedRegex(@"(₹|\brs\.?|\binr)?\s*(\d+(?:[.,]\d+)*)\s*(crores|crore|cr|lakhs|lakh|lacs|lac|l|thousand|k)?\b")]
    private static partial Regex Amount();

    [GeneratedRegex(@"(\d{1,2})\s*(?:/\s*(\d{1,2}))?\s*bhk", RegexOptions.IgnoreCase)]
    private static partial Regex Bhks();

    [GeneratedRegex(@"\d{1,2}")]
    private static partial Regex Digits();
}

/// <summary>Budget bands (D34): sale <c>Up to 25L</c> … <c>Above 5Cr</c>; rent (a month) <c>Up to 10k</c> … <c>Above 1L</c>. Null = no budget.</summary>
public static class BudgetBands
{
    private static readonly (decimal Upto, string Label)[] Sale =
    [
        (2_500_000m, "Up to 25L"), (5_000_000m, "25L-50L"), (10_000_000m, "50L-1Cr"), (20_000_000m, "1Cr-2Cr"), (50_000_000m, "2Cr-5Cr"),
    ];

    private static readonly (decimal Upto, string Label)[] Rent =
    [
        (10_000m, "Up to 10k"), (25_000m, "10k-25k"), (50_000m, "25k-50k"), (100_000m, "50k-1L"),
    ];

    /// <summary>The band of the budget's top (else its bottom). Without a purpose a sum of 5 lakh or more is read as a sale.</summary>
    public static string? Of(PropertyPurpose? purpose, decimal? min, decimal? max)
    {
        if ((max ?? min) is not { } amount || amount <= 0)
            return null;
        var rent = purpose == PropertyPurpose.Rent || (purpose is null && amount < 500_000m);
        var bands = rent ? Rent : Sale;
        foreach (var (upto, label) in bands)
            if (amount <= upto)
                return label;
        return rent ? "Above 1L" : "Above 5Cr";
    }
}
