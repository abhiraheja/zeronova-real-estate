using System.Globalization;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Customers;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Application.Features.Requirements;

/// <summary>
/// Website searches that found nothing (D34, E2's empty-search log): the criteria and the India day, counted — never who
/// searched (no IP, no browser, no text search: free text may hold a name or a number). A search with no criteria at all is
/// not kept. A failure is logged and never fails the search.
/// </summary>
public sealed class DemandSignals(IDemandSignalRepository signals, TimeProvider clock, ILogger<DemandSignals> logger)
{
    public async Task RecordEmptySearchAsync(string companyId, PropertyPurpose? purpose, IReadOnlyList<PropertyType>? types, string? city, string? locality,
        decimal? priceFrom, decimal? priceTo, IReadOnlyList<int>? bhk, CancellationToken ct)
    {
        try
        {
            var typeList = (types ?? []).Distinct().Order().ToList();
            var bhkList = (bhk ?? []).Where(b => b is > 0 and <= 20).Distinct().Order().ToList();
            var cityName = EstateText.Clean(city);
            // A "Popular localities" tile sends a slug: "sector-70" is "sector 70".
            var place = EstateText.Clean(locality?.Replace('-', ' '));
            var band = BudgetBands.Of(purpose, priceFrom, priceTo);
            if (purpose is null && typeList.Count == 0 && cityName is null && place is null && band is null && bhkList.Count == 0)
                return;
            var localityKey = place is null ? null : EstateText.Key(place);
            var key = string.Join('|', purpose?.ToString() ?? "*", string.Join(',', typeList), cityName?.ToLowerInvariant() ?? "*", localityKey ?? "*", band ?? "*",
                string.Join(',', bhkList));
            await signals.RecordAsync(new DemandSignalDocument
            {
                Id = string.Empty, Day = IndiaTime.Text(IndiaTime.Today(clock)), Key = key, Purpose = purpose, Types = typeList, City = cityName,
                Locality = place, LocalityKey = localityKey, BudgetBand = band, Bhk = bhkList,
            }, companyId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "An empty website search of company {CompanyId} was not counted.", companyId);
        }
    }
}

/// <summary>
/// The unmet-demand report (D34), grouped by locality, type, BHK and budget band (each within its purpose), so the dealer
/// knows what to source. As they stand now: (a) watched requirements, (b) active requirements with no match at all for
/// <paramref name="UnmatchedDays"/> days or more. In the window (India days <paramref name="From"/> to <paramref name="To"/>,
/// both included; default the last 90 days): (c) requirements closed not available (by code, or the old note), (d)
/// enquiries closed not available that made no watched requirement (one that did is counted as its requirement, not twice),
/// (e) website searches that found nothing. Requirements and enquiries follow the agent rules (one's own, everyone's with
/// PRQV); the searches are the company's and know nobody.
/// </summary>
public sealed record GetDemandReportQuery(string? From, string? To, IReadOnlyList<DemandGroupBy>? GroupBy, int UnmatchedDays = 14) : IRequest<DemandReportDto>;

public sealed class GetDemandReportHandler(
    IRequirementRepository requirements, IMatchRepository matches, IEnquiryRepository enquiries, IDemandSignalRepository signals, IEstateSettingsRepository settings,
    WatchedDemand watched, EstateScope scope, TimeProvider clock)
    : IRequestHandler<GetDemandReportQuery, DemandReportDto>
{
    private const int MaxDays = 400;
    private const int MaxGroups = 300;
    private const int MaxSamples = 5;
    private const string Any = "Any";

    /// <summary>One piece of demand: where it came from, what it asks, who (none for a search) and how many it stands for.</summary>
    private sealed record Item(DemandSource Source, PropertyPurpose? Purpose, DemandCriteria Criteria, DemandSampleDto? Sample, int Weight, string? Band,
        IReadOnlyList<string> LocalityKeys);

    public async Task<DemandReportDto> Handle(GetDemandReportQuery request, CancellationToken ct)
    {
        var to = Day(request.To, "To") ?? IndiaTime.Today(clock);
        var from = Day(request.From, "From") ?? to.AddDays(-89);
        if (from > to)
            throw new ValidatorException("From must not be after To.");
        if (to.DayNumber - from.DayNumber >= MaxDays)
            throw new ValidatorException($"At most {MaxDays} days at a time.");
        var unmatchedDays = Math.Clamp(request.UnmatchedDays, 1, 365);
        var dims = request.GroupBy is { Count: > 0 } g ? g.Distinct().Order().ToList() : [.. Enum.GetValues<DemandGroupBy>()];
        var fromUtc = IndiaTime.StartUtc(from);
        var endUtc = IndiaTime.StartUtc(to.AddDays(1));
        var now = clock.GetUtcNow().UtcDateTime;
        var s = await EstateSettings.LoadAsync(settings, ct);

        var mine = requirements.GetDocuments();
        if (!scope.SeesAllRequirements)
        {
            var me = scope.UserId;
            mine = mine.Where(r => r.AgentId == me);
        }
        var items = new List<Item>();
        var names = new Dictionary<string, string>();

        // (a) watched, now.
        foreach (var r in await mine.Where(r => r.Status == RequirementStatus.Watching).ToListAsync(ct))
            items.Add(Of(DemandSource.Watching, r, names));

        // (b) active with no match at all, made at least the days ago.
        var cut = now.AddDays(-unmatchedDays);
        var active = (await mine.Where(r => r.Status == RequirementStatus.Active).ToListAsync(ct)).Where(r => Created(r) <= cut).ToList();
        if (active.Count > 0)
        {
            var ids = active.Select(r => r.Id).ToList();
            var matched = (await matches.GetDocuments(m => ids.Contains(m.RequirementId)).Select(m => m.RequirementId).Distinct().ToListAsync(ct)).ToHashSet();
            items.AddRange(active.Where(r => !matched.Contains(r.Id)).Select(r => Of(DemandSource.Unmatched, r, names)));
        }

        // (c) closed not available in the window (a row from before C3 by its note, and its last change for the day).
        var closed = await mine.Where(r => r.Status == RequirementStatus.Closed
                && (r.CloseReasonCode == RequirementCloseReason.NotAvailable || r.CloseReasonCode == null)).ToListAsync(ct);
        foreach (var r in closed.Where(CustomerRules.IsNotAvailable))
            if ((r.ClosedAt ?? (r.UpdatedBy?.ChangedDate ?? r.CreatedBy?.ChangedDate)?.UtcDateTime) is { } at && at >= fromUtc && at < endUtc)
                items.Add(Of(DemandSource.ClosedNotAvailable, r, names));

        // (d) enquiries closed not available in the window, unless they made a watched requirement (counted above as that).
        var enquiryQuery = enquiries.GetDocuments(e => e.Status == EnquiryStatus.Closed && e.CloseReason == EnquiryCloseReason.NotAvailable
            && e.ClosedAt >= fromUtc && e.ClosedAt < endUtc);
        if (!scope.SeesAllRequirements)
        {
            var me = scope.UserId;
            enquiryQuery = enquiryQuery.Where(e => e.AgentId == me);
        }
        var closedEnquiries = await enquiryQuery.ToListAsync(ct);
        var madeFrom = closedEnquiries.Select(e => e.Id).ToList();
        var watchedFor = madeFrom.Count == 0
            ? []
            : (await requirements.GetDocuments(r => madeFrom.Contains(r.EnquiryId!)).Select(r => r.EnquiryId).ToListAsync(ct)).OfType<string>().ToHashSet();
        foreach (var e in closedEnquiries.Where(e => !watchedFor.Contains(e.Id)))
        {
            var c = await watched.CriteriaAsync(e, s, ct);
            var sample = new DemandSampleDto(DemandSource.EnquiryNotAvailable, e.Id, e.Code, e.Name, e.LeadId, e.ContactId, e.AgentId,
                e.Wanted ?? e.PropertyTitle ?? e.ProjectName ?? "—");
            items.Add(Make(DemandSource.EnquiryNotAvailable, c, sample, 1, names));
        }

        // (e) website searches that found nothing, counted per day and criteria.
        var fromDay = IndiaTime.Text(from);
        var toDay = IndiaTime.Text(to);
        foreach (var x in await signals.GetDocuments(x => x.Day.CompareTo(fromDay) >= 0 && x.Day.CompareTo(toDay) <= 0).ToListAsync(ct))
        {
            var c = new DemandCriteria(x.Purpose ?? PropertyPurpose.Sale, x.Types, x.City, x.Locality is null ? [] : [x.Locality], x.Bhk, null, null);
            items.Add(new Item(DemandSource.WebsiteSearch, x.Purpose, c, null, x.Count, x.BudgetBand,
                x.Locality is null ? [] : [x.LocalityKey ?? EstateText.Key(x.Locality)]));
            if (x.Locality is not null)
                names.TryAdd(x.LocalityKey ?? EstateText.Key(x.Locality), x.Locality);
        }

        // Grouping: every row in every group its criteria fall in.
        var groups = new Dictionary<string, (PropertyPurpose? Purpose, string? Locality, string? Type, string? Bhk, string? Band, Dictionary<DemandSource, int> Counts,
            List<DemandSampleDto> Samples)>();
        foreach (var item in items)
        {
            var purpose = item.Purpose;
            IReadOnlyList<string?> none = [null];
            IReadOnlyList<string?> any = [Any];
            var localities = !dims.Contains(DemandGroupBy.Locality) ? none : item.LocalityKeys.Count == 0 ? any : [.. item.LocalityKeys];
            var types = !dims.Contains(DemandGroupBy.Type) ? none : item.Criteria.Types.Count == 0 ? any : [.. item.Criteria.Types.Select(t => t.ToString())];
            var bhks = !dims.Contains(DemandGroupBy.Bhk) ? none
                : item.Criteria.Bhk.Count == 0 ? any : [.. item.Criteria.Bhk.Select(b => b.ToString(CultureInfo.InvariantCulture))];
            var band = dims.Contains(DemandGroupBy.BudgetBand) ? item.Band ?? Any : null;
            foreach (var locality in localities.Distinct())
                foreach (var type in types.Distinct())
                    foreach (var bhk in bhks.Distinct())
                    {
                        var key = $"{purpose}|{locality}|{type}|{bhk}|{band}";
                        if (!groups.TryGetValue(key, out var group))
                            groups[key] = group = (purpose, locality is null or Any ? locality : names.GetValueOrDefault(locality, locality), type, bhk, band, [], []);
                        group.Counts[item.Source] = group.Counts.GetValueOrDefault(item.Source) + item.Weight;
                        if (item.Sample is { } sample && group.Samples.Count < MaxSamples && group.Samples.All(x => x.Id != sample.Id))
                            group.Samples.Add(sample);
                    }
        }

        var totals = items.GroupBy(i => i.Source).OrderBy(x => x.Key).Select(x => new DemandSourceCountDto(x.Key, x.Sum(i => i.Weight))).ToList();
        var rows = groups.Values
            .Select(x => new DemandGroupDto(x.Purpose, x.Locality, x.Type, x.Bhk, x.Band, x.Counts.Values.Sum(),
                [.. x.Counts.OrderBy(c => c.Key).Select(c => new DemandSourceCountDto(c.Key, c.Value))], x.Samples))
            .OrderByDescending(x => x.Total).ThenBy(x => x.Locality, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Type).ThenBy(x => x.Bhk).ThenBy(x => x.BudgetBand)
            .Take(MaxGroups).ToList();
        return new DemandReportDto(fromDay, toDay, dims, unmatchedDays, totals, rows);
    }

    private static Item Of(DemandSource source, RequirementDocument r, Dictionary<string, string> names) =>
        Make(source, DemandCriteria.Of(r), new DemandSampleDto(source, r.Id, null, r.LeadName, r.LeadId, r.ContactId, r.AgentId, CustomerRules.Summary(r)), 1, names);

    private static Item Make(DemandSource source, DemandCriteria c, DemandSampleDto sample, int weight, Dictionary<string, string> names)
    {
        foreach (var l in c.Localities)
            names.TryAdd(EstateText.Key(l), l);
        return new Item(source, c.Purpose, c, sample, weight, BudgetBands.Of(c.Purpose, c.BudgetMin, c.BudgetMax), [.. c.Localities.Select(EstateText.Key).Distinct()]);
    }

    private static DateTime Created(RequirementDocument r) =>
        r.CreatedBy?.ChangedDate.UtcDateTime ?? (ObjectId.TryParse(r.Id, out var oid) ? oid.CreationTime : DateTime.MinValue);

    private static DateOnly? Day(string? value, string field) =>
        EstateText.Date(value, field) is { } d ? DateOnly.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
}
