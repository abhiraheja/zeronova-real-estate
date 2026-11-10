using System.Globalization;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Money;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Settings;

namespace ZeroNovaRealEstate.Application.Features.Deals;

/// <summary>
/// What the checks need beyond the deal, its property and its people (C5): the property's state, the dealer's
/// settings, the deal's brokerages (null when the caller may not see brokerage — PBRV/PBRM), and today in India.
/// </summary>
public sealed record DealCheckContext(string? State, EstateSettingsDocument Settings, IReadOnlyList<BrokerageDocument>? Brokerages, string Today)
{
    public static DealCheckContext For(PropertyDocument property, EstateSettingsDocument settings, IReadOnlyList<BrokerageDocument>? brokerages, string today) =>
        new(EstateStates.Of(property, settings), settings, brokerages, today);
}

/// <summary>Loads the settings and, for a caller who may see brokerage, the deals' brokerages — what <see cref="DealCheckContext"/> needs.</summary>
public sealed class DealCheckSource(IEstateSettingsRepository settings, IBrokerageRepository brokerages, EstateScope scope, TimeProvider clock)
{
    public async Task<(EstateSettingsDocument Settings, ILookup<string, BrokerageDocument>? Brokerages, string Today)> LoadAsync(
        IReadOnlyCollection<string> dealIds, CancellationToken ct)
    {
        var s = await EstateSettings.LoadAsync(settings, ct);
        ILookup<string, BrokerageDocument>? rows = null;
        if (scope.Has(EstatePermissions.BrokerageView) || scope.Has(EstatePermissions.BrokerageManage))
            rows = (await brokerages.GetDocuments(b => dealIds.Contains(b.DealId)).ToListAsync(ct)).ToLookup(b => b.DealId);
        return (s, rows, EstateText.Today(clock));
    }
}

/// <summary>
/// The property TDS (C5, D38): s.393(1) of the Income-tax Act 2025 for payments on or after 1 April 2026 (s.194-IA
/// before); due when the whole deal — every buyer and seller together — is ₹50 lakh or more; deducted from every
/// instalment, the token included; 1% of each resident seller's share, 20% without a PAN. A non-resident seller's share
/// falls under s.195 instead: no rate is worked out for it.
/// </summary>
public static class PropertyTds
{
    public const decimal Threshold = 50_00_000m;
    public const string NewActFrom = "2026-04-01";
    public const string NewSection = "s.393(1) of the Income-tax Act 2025";
    public const string OldSection = "s.194-IA";
    public const string NonResidentText = "TDS under s.195 — buyer needs a TAN and files Form 27Q; rate depends on capital gains — consult a CA";
    public const string ResidentNote = "The buyer deducts it from every payment, the token included, deposits it with Form 26QB within 30 days and gives the seller Form 16B.";

    /// <summary>The section for a payment dated <paramref name="date"/> (yyyy-MM-dd; none = today).</summary>
    public static string Section(string? date, string today) => string.CompareOrdinal(date ?? today, NewActFrom) >= 0 ? NewSection : OldSection;

    public static bool NonResident(PartyDocument? party) => party?.Residency is PartyResidency.Nri or PartyResidency.Oci or PartyResidency.ForeignNational;

    public static string ResidencyLabel(PartyResidency? residency) => residency switch
    {
        PartyResidency.Nri => "NRI",
        PartyResidency.Oci => "OCI",
        PartyResidency.ForeignNational => "foreign national",
        _ => "resident",
    };

    /// <param name="ResidentDue">The 1% (or 20%) TDS is due: the deal is ₹50 lakh or more and a resident seller (or none named yet) is paid.</param>
    /// <param name="RatePercent">Over the whole of each payment: Σ resident share × rate. Null when nothing is worked out.</param>
    public sealed record Plan(bool Sale, bool ResidentDue, decimal? RatePercent, IReadOnlyList<DealTdsSellerDto> Sellers, bool NonResidentSeller)
    {
        public bool Applies => ResidentDue || NonResidentSeller;

        public decimal? On(decimal amount) => ResidentDue && RatePercent is { } r ? Math.Round(amount * r / 100m, 0) : null;
    }

    public static Plan For(DealDocument d, PropertyDocument p, IReadOnlyDictionary<string, PartyDocument> people)
    {
        if (d.Kind != PropertyPurpose.Sale)
            return new Plan(false, false, null, [], false);
        var shares = Shares(d, p);
        var sellers = d.SellerIds.Select(id =>
        {
            var x = people.GetValueOrDefault(id);
            var nonResident = NonResident(x);
            var noPan = x?.PanLast4 is null;
            return new DealTdsSellerDto(id, x?.Name ?? "—", shares[id], nonResident, noPan, nonResident ? null : noPan ? 20m : 1m);
        }).ToList();
        var residentDue = d.Price >= Threshold && (sellers.Count == 0 || sellers.Any(s => !s.NonResident));
        decimal? rate = !residentDue ? null
            : sellers.Count == 0 ? 1m
            : Math.Round(sellers.Where(s => !s.NonResident).Sum(s => s.SharePercent * s.RatePercent!.Value / 100m), 4);
        return new Plan(true, residentDue, rate, sellers, sellers.Any(s => s.NonResident));
    }

    /// <summary>Each seller's share: their share as owners when every seller is a current owner, else equal.</summary>
    private static Dictionary<string, decimal> Shares(DealDocument d, PropertyDocument p)
    {
        var owned = d.SellerIds.Select(id => p.Owners.FirstOrDefault(o => o.PartyId == id)?.SharePercent ?? 0m).ToList();
        var total = owned.Sum();
        return d.SellerIds.Select((id, i) => (id, share: d.SellerIds.Count == 0 ? 0m
                : owned.All(x => x > 0) && total > 0 ? Math.Round(owned[i] * 100m / total, 4) : Math.Round(100m / d.SellerIds.Count, 4)))
            .GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.First().share);
    }

    public static DealTdsDto? Dto(Plan plan, DealDocument d, string today)
    {
        if (!plan.Sale)
            return null;
        var section = Section(d.ExecutionDate, today);
        var notes = new List<string>();
        if (plan.ResidentDue)
            notes.Add(ResidentNote);
        if (plan.NonResidentSeller)
            notes.Add(NonResidentText + ".");
        return new DealTdsDto(plan.Applies, Threshold, section, plan.RatePercent, plan.On(d.Price), plan.NonResidentSeller,
            notes.Count == 0 ? null : string.Join(' ', notes), plan.Sellers);
    }
}

/// <summary>
/// Brokerage TDS (C5, D38): s.393(1) (Table Sl. 1(ii)) of the Income-tax Act 2025 from 1 April 2026, s.194H before; 2%,
/// or 20% when the dealer has no PAN on record; only once the payer's brokerage to the dealer in the financial year is
/// over ₹20,000; and generally not at all when the payer is an individual or HUF.
/// </summary>
public static class BrokerageTds
{
    public const decimal YearlyThreshold = 20_000m;
    public const string NewSection = "s.393(1) (Table Sl. 1(ii)) of the Income-tax Act 2025";
    public const string OldSection = "s.194H";

    public const string IndividualNote =
        "The payer is an individual or HUF: TDS on brokerage generally does not apply, so none is shown. Mark the payer a company or firm if they deduct it.";

    public static string Section(string date) => string.CompareOrdinal(date, PropertyTds.NewActFrom) >= 0 ? NewSection : OldSection;

    /// <summary>The dealer's PAN is on record: their own, or inside their GSTIN (characters 3–12).</summary>
    public static bool DealerHasPan(EstateSettingsDocument s) => s.DealerPan is not null || s.Gstin is { Length: 15 };

    /// <summary>The Indian financial year (April–March) a yyyy-MM-dd day falls in, as its first year.</summary>
    public static int FinancialYear(string day)
    {
        var d = DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return d.Month >= 4 ? d.Year : d.Year - 1;
    }

    /// <summary>The India day a brokerage was invoiced.</summary>
    public static string InvoiceDay(BrokerageDocument b) =>
        DateOnly.FromDateTime(b.CreatedAt.AddMinutes(330)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// Haryana's brokerage cap (C5, D38): the Haryana Regulation of Property Dealers and Consultants Rules 2009, rule 10 —
/// 1% of the agreed consideration, shared between buyer and seller (½% each). Warned, never blocked; only on sales.
/// </summary>
public static class HaryanaBrokerageCap
{
    /// <param name="only">One brokerage's screen: the deal's total and that side only; null = every side.</param>
    public static IReadOnlyList<string> Warnings(DealDocument d, string? state, IEnumerable<BrokerageDocument> rows, BrokerageDocument? only = null)
    {
        if (!EstateStates.Is(state, EstateStates.Haryana) || d.Kind != PropertyPurpose.Sale || d.Price <= 0)
            return [];
        var live = rows.Where(b => b.DealId == d.Id && !b.WrittenOff).ToList();
        var w = new List<string>();
        var total = live.Sum(b => b.Amount);
        if (total > d.Price / 100m)
            w.Add($"Haryana caps brokerage at 1% of the deal value, half from each side (Haryana property dealers rules 2009, rule 10) — "
                + $"the brokerage on {d.Code} comes to {Percent(total, d.Price)}% ({IndianWords.Figures(total)}).");
        foreach (var side in live.Where(b => b.Amount > d.Price / 200m && (only is null || b.Id == only.Id)))
            w.Add($"The {side.Side.ToString().ToLowerInvariant()}'s brokerage on {d.Code} is {Percent(side.Amount, d.Price)}% ({IndianWords.Figures(side.Amount)}) — "
                + "Haryana allows 0.5% from each side.");
        return w;
    }

    private static string Percent(decimal amount, decimal price) => (amount * 100m / price).ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>FEMA (C5, D38): an NRI, OCI or foreign national may not buy agricultural land, a plantation or a farmhouse — only inherit it.</summary>
public static class FemaCheck
{
    public static bool FarmLand(PropertyType type) => type is PropertyType.AgriculturalLand or PropertyType.Farmhouse;

    public static IReadOnlyList<string> Warnings(DealDocument d, PropertyDocument p, IReadOnlyDictionary<string, PartyDocument> people)
    {
        if (d.Kind != PropertyPurpose.Sale || !FarmLand(p.Type))
            return [];
        return [.. d.BuyerIds.Select(id => people.GetValueOrDefault(id)).OfType<PartyDocument>().Where(PropertyTds.NonResident)
            .Select(b => $"{b.Name} is an {PropertyTds.ResidencyLabel(b.Residency)} buyer — under FEMA an NRI, OCI or foreign national cannot buy "
                + $"{(p.Type == PropertyType.Farmhouse ? "a farmhouse" : "agricultural land")} (only inherit it). Do not go ahead without legal advice.")];
    }
}

/// <summary>GPA (C5, D38): Suraj Lamp & Industries v. State of Haryana (2011) — a GPA sale passes no title; only a registered conveyance does.</summary>
public static class GpaCheck
{
    public const string Text = "GPA gives no title (Supreme Court, Suraj Lamp 2011) — check the chain";

    public static IReadOnlyList<string> Warnings(PropertyDocument p, IReadOnlyDictionary<string, PartyDocument> people)
    {
        var w = p.Owners.Where(o => o.Holding == HoldingKind.PowerOfAttorney)
            .Select(o => $"{people.GetValueOrDefault(o.PartyId)?.Name ?? "An owner"} holds the property by GPA. {Text}.").ToList();
        if (w.Count == 0 && p.Tenure == LandTenure.PowerOfAttorney)
            w.Add($"The property is held on a GPA. {Text}.");
        return w;
    }
}

/// <summary>
/// The stamp duty estimate on a sale (C5) — an estimate only: the dealer's rates for the property's state (urban or
/// rural), by the buyers' genders (every buyer a woman: the women's rate; men and women: joint; else men), on the deal price.
/// </summary>
public static class StampDuty
{
    public const string Label =
        "Estimate only — duty is charged on the deal price or the collector rate, whichever is higher, at the rates in force on the registry date. Check with the Sub-Registrar.";

    /// <summary>The shipped rates, from secondary sources (Kotak, Housing.com, Aditya Birla Capital); Chandigarh blank (unverified).</summary>
    public static IReadOnlyList<StampDutyRateModel> Defaults() =>
    [
        new() { State = EstateStates.Punjab, MenPercent = 7m, WomenPercent = 5m, JointPercent = 6m, RegistrationPercent = 1m,
            Note = "Women's rate only when the property is in women's names alone. From secondary sources — check the current notification." },
        new() { State = EstateStates.Haryana, Area = StampDutyArea.Urban, MenPercent = 7m, WomenPercent = 5m, JointPercent = 6m, RegistrationPercent = 1m,
            Note = "Registration about 1%. From secondary sources — check the current notification." },
        new() { State = EstateStates.Haryana, Area = StampDutyArea.Rural, MenPercent = 5m, WomenPercent = 3m, JointPercent = 4m, RegistrationPercent = 1m,
            Note = "Registration about 1%. From secondary sources — check the current notification." },
        new() { State = EstateStates.Chandigarh, Note = "Not verified — enter the current rates." },
    ];

    public static IReadOnlyList<StampDutyRateModel> Rates(EstateSettingsDocument s) => s.StampDutyRates is { Count: > 0 } own ? own : Defaults();

    public static StampDutyEstimateDto? Estimate(DealDocument d, PropertyDocument p, IReadOnlyDictionary<string, PartyDocument> people, DealCheckContext x)
    {
        if (d.Kind != PropertyPurpose.Sale)
            return null;
        var notes = new List<string>();
        var rates = Rates(x.Settings).Where(r => EstateStates.Is(r.State, x.State ?? string.Empty)).ToList();
        var area = p.Rural ? StampDutyArea.Rural : StampDutyArea.Urban;
        var row = rates.FirstOrDefault(r => r.Area == area) ?? rates.FirstOrDefault(r => r.Area is null);
        if (!p.Rural && rates.Any(r => r.Area == StampDutyArea.Rural))
            notes.Add("Counted as urban — mark the property rural if it is outside municipal limits.");

        var buyers = d.BuyerIds.Select(id => people.GetValueOrDefault(id)).OfType<PartyDocument>().ToList();
        if (buyers.Count == 0)
            notes.Add("No buyer yet — the men's rate is used.");
        foreach (var b in buyers.Where(b => b.Gender is null or PartyGender.Other))
            notes.Add(b.Gender is null ? $"{b.Name}'s gender is not set — counted at the men's rate." : $"{b.Name} is counted at the men's rate.");
        var women = buyers.Count(b => b.Gender == PartyGender.Female);
        var category = buyers.Count > 0 && women == buyers.Count ? "Women" : women > 0 ? "Joint" : "Men";

        decimal? percent = null, registration = null;
        if (x.State is null)
            notes.Add("The property's state is not set — set it on the property or the company's default state in the settings.");
        else if (row is null)
            notes.Add($"No stamp duty rates for {x.State} in the settings — add them.");
        else
        {
            percent = category switch { "Women" => row.WomenPercent, "Joint" => row.JointPercent, _ => row.MenPercent };
            registration = row.RegistrationPercent;
            if (percent is null)
                notes.Add($"The {category.ToLowerInvariant()} rate for {x.State} is not set — add it in the settings.");
            if (row.Note is not null)
                notes.Add(row.Note);
        }
        decimal? duty = percent is { } pd ? Math.Round(d.Price * pd / 100m, 0) : null;
        decimal? fee = registration is { } pr ? Math.Round(d.Price * pr / 100m, 0) : null;
        return new StampDutyEstimateDto(Label, x.State, p.Rural, category, percent, duty, registration, fee,
            duty is null && fee is null ? null : (duty ?? 0) + (fee ?? 0), notes);
    }
}
