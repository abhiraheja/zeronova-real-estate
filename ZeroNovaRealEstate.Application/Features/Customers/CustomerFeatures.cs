using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Deals;
using ZeroNovaRealEstate.Application.Features.Enquiries;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Features.Rentals;
using ZeroNovaRealEstate.Application.Features.Requirements;
using ZeroNovaRealEstate.Application.Features.Visits;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Configuration;
using ZeroNovaRealEstate.Models.Customers;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Visits;

namespace ZeroNovaRealEstate.Application.Features.Customers;

/// <summary>
/// "Highlighted" (D32, D33): a new match on an active requirement within the last <see cref="Days"/> days — the caller's own
/// matches, or everyone's with PRQV. C3 adds revived matches on watched requirements.
/// </summary>
public static class CustomerHighlights
{
    public const int Days = 7;

    public static async Task<HashSet<string>> HotRequirementIdsAsync(IMatchRepository matches, IRequirementRepository requirements, EstateScope scope,
        TimeProvider clock, IReadOnlyCollection<string>? among, CancellationToken ct)
    {
        var cut = clock.GetUtcNow().UtcDateTime.AddDays(-Days);
        var query = matches.GetDocuments(m => m.Status == MatchStatus.New && m.CreatedAt >= cut);
        if (!scope.SeesAllRequirements)
        {
            var me = scope.UserId;
            query = query.Where(m => m.AgentId == me);
        }
        if (among is not null)
        {
            var ids = among.ToList();
            query = query.Where(m => ids.Contains(m.RequirementId));
        }
        var candidates = await query.Select(m => m.RequirementId).Distinct().ToListAsync(ct);
        if (candidates.Count == 0)
            return [];
        return [.. await requirements.GetDocuments(r => candidates.Contains(r.Id) && r.Status == RequirementStatus.Active).Select(r => r.Id).ToListAsync(ct)];
    }
}

// ── The Customers list (D32) ──────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The customers Real Estate knows, the latest activity first: an agent (PRQM) sees the customers they are agent of any row
/// of, with their own share of the numbers; a manager (PRQV) every customer with all of them. Read from the
/// <c>re_customers</c> summaries (one indexed, paged query); a company's first list builds them from its rows.
/// </summary>
public sealed record GetCustomersQuery(EstateCustomerFilterInput Filter, int Skip = 0, int Take = 50) : IRequest<EstateCustomerPageDto>;

public sealed class GetCustomersHandler(ICustomerRepository customers, CustomerBook book, IMatchRepository matches, IRequirementRepository requirements,
    EstateScope scope, TimeProvider clock)
    : IRequestHandler<GetCustomersQuery, EstateCustomerPageDto>
{
    public async Task<EstateCustomerPageDto> Handle(GetCustomersQuery request, CancellationToken ct)
    {
        await book.EnsureBuiltAsync(ct);
        var f = request.Filter;
        var all = scope.SeesAllRequirements;
        var me = scope.UserId;
        var hot = await CustomerHighlights.HotRequirementIdsAsync(matches, requirements, scope, clock, null, ct);
        var hotList = hot.ToList();

        var query = customers.GetDocuments();
        if (!all)
            query = query.Where(c => c.AgentIds.Contains(me));
        else if (EstateText.Clean(f.AgentId) is { } agent)
            query = query.Where(c => c.AgentIds.Contains(agent));

        foreach (var word in (f.Text ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6))
        {
            var digits = new string(word.Where(char.IsDigit).ToArray());
            var term = digits.Length >= 4 && digits.Length == word.TrimStart('+').Length ? digits : word;
            query = query.Where(c => c.SearchText.Contains(term));
        }

        if (f.HasOpenRequirement is { } open)
            query = all
                ? open ? query.Where(c => c.Totals.OpenRequirements > 0) : query.Where(c => c.Totals.OpenRequirements == 0)
                : open ? query.Where(c => c.Agents.Any(a => a.AgentId == me && a.OpenRequirements > 0))
                    : query.Where(c => !c.Agents.Any(a => a.AgentId == me && a.OpenRequirements > 0));

        if (f.LastActivityFrom is not null || f.LastActivityTo is not null)
        {
            var from = f.LastActivityFrom?.UtcDateTime ?? DateTime.MinValue;
            var to = f.LastActivityTo?.UtcDateTime ?? DateTime.MaxValue;
            query = all
                ? query.Where(c => c.LastActivityAt >= from && c.LastActivityAt < to)
                : query.Where(c => c.Agents.Any(a => a.AgentId == me && a.LastActivityAt >= from && a.LastActivityAt < to));
        }

        if (f.Highlighted is { } highlighted)
            query = highlighted
                ? query.Where(c => c.RequirementIds.Any(r => hotList.Contains(r)))
                : query.Where(c => !c.RequirementIds.Any(r => hotList.Contains(r)));

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(c => c.LastActivityAt).ThenByDescending(c => c.Id)
            .Skip(Math.Max(0, request.Skip)).Take(Math.Clamp(request.Take, 1, 200)).ToListAsync(ct);
        return new EstateCustomerPageDto(total, [.. rows.Select(c => ToDto(c, hot, all, me))]);
    }

    private EstateCustomerDto ToDto(CustomerDocument c, HashSet<string> hot, bool all, string me)
    {
        var mine = c.Agents.FirstOrDefault(a => a.AgentId == me) ?? new CustomerCountsModel { AgentId = me };
        var seen = all ? c.Totals : mine;
        int? deals = scope.SeesAllDeals ? c.Totals.Deals : scope.MayDeals ? mine.Deals : null;
        return new EstateCustomerDto(c.ContactId, c.LeadIds, c.Name, c.Phone ?? (scope.SeesOwners ? c.PartyPhone : null), c.AgentId, c.AgentName,
            new EstateCustomerCountsDto(seen.Enquiries, seen.OpenRequirements, seen.Visits, deals), Utc(seen.LastActivityAt), c.RequirementIds.Any(hot.Contains),
            seen.Unmet);
    }

    internal static DateTimeOffset? Utc(DateTime? at) => at is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)) : null;
}

// ── The customer's dashboard (D32) ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Real Estate side of a customer's dashboard: by the contact and / or the leads the app read from zeronova-lead (our
/// own rows add the leads they know). Every list follows its permission (see <see cref="EstateCustomerDashboardDto"/>); a
/// customer another agent works shows an agent only what is theirs.
/// </summary>
public sealed record GetCustomerQuery(string? ContactId, IReadOnlyList<string>? LeadIds) : IRequest<EstateCustomerDashboardDto>;

public sealed class GetCustomerHandler(
    CustomerBook book, IMatchRepository matches, IShareRepository shares, IPropertyRepository properties, IProjectRepository projects,
    IRequirementRepository requirements, VisitMapper visitMapper, DealMapper dealMapper, TenancyMapper tenancyMapper, EstateScope scope, ShareOptions options,
    TimeProvider clock)
    : IRequestHandler<GetCustomerQuery, EstateCustomerDashboardDto>
{
    private const int MaxMatches = 200;

    public async Task<EstateCustomerDashboardDto> Handle(GetCustomerQuery request, CancellationToken ct)
    {
        var id = await book.ResolveAsync(request.ContactId, request.LeadIds, ct) ?? throw new ValidatorException("Pick the customer (contact or lead).");
        var rows = await book.LoadAsync(id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var all = scope.SeesAllRequirements;
        var me = scope.UserId;
        bool Mine(string? agent) => all || agent == me;

        var enquiryRows = rows.Enquiries.Where(e => Mine(e.AgentId)).OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).ToList();
        var requirementRows = rows.Requirements.Where(r => Mine(r.AgentId)).OrderByDescending(r => r.Id).ToList();
        var requirementIds = requirementRows.Select(r => r.Id).ToList();
        var visitRows = rows.Visits.Where(v => Mine(v.AgentId)).OrderByDescending(v => v.At).ToList();

        var matchQuery = matches.GetDocuments(m => requirementIds.Contains(m.RequirementId));
        if (!all)
            matchQuery = matchQuery.Where(m => m.AgentId == me);
        var matchRows = requirementIds.Count == 0 ? [] : await matchQuery.ToListAsync(ct);
        var shareRows = requirementIds.Count == 0
            ? []
            : await shares.GetDocuments(s => requirementIds.Contains(s.RequirementId!)).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);

        var dealRows = scope.MayDeals ? rows.Deals.Where(d => scope.SeesAllDeals || d.AgentId == me).OrderByDescending(d => d.Id).ToList() : null;
        var tenancyRows = scope.MayRentals ? rows.Tenancies.Where(t => scope.SeesAllRentals || t.AgentId == me).OrderByDescending(t => t.Id).ToList() : null;
        var partyIds = rows.Parties.Select(p => p.Id).ToList();
        List<PropertyDocument>? owned = scope.SeesOwners && partyIds.Count > 0
            ? await properties.GetDocuments(p => p.Owners.Any(o => partyIds.Contains(o.PartyId))).OrderByDescending(p => p.Id).ToListAsync(ct)
            : scope.SeesOwners ? [] : null;

        var requirementDtos = await RequirementMapping.ManyAsync(requirementRows, matches, ct);
        var matchTotals = matchRows.GroupBy(m => m.RequirementId).ToDictionary(g => g.Key, g => g.Count());
        var activeRequirements = requirementRows.Where(r => r.Status == RequirementStatus.Active).Select(r => r.Id).ToHashSet();
        var highlighted = matchRows.Any(m => m.Status == MatchStatus.New && m.CreatedAt >= now.AddDays(-CustomerHighlights.Days) && activeRequirements.Contains(m.RequirementId));

        var shownMatches = matchRows.OrderBy(m => m.Status).ThenByDescending(m => m.CreatedAt).Take(MaxMatches).ToList();
        var unmet = requirementRows.Where(CustomerRules.IsUnmet)
            .Select(r => new CustomerUnmetDto(CustomerUnmetKind.Requirement, r.Id, null, CustomerRules.Summary(r), r.CloseReason,
                GetCustomersHandler.Utc((r.UpdatedBy?.ChangedDate ?? r.CreatedBy?.ChangedDate)?.UtcDateTime), r.AgentId, r.Status, null))
            .Concat(enquiryRows.Where(CustomerRules.IsUnmet)
                .Select(e => new CustomerUnmetDto(CustomerUnmetKind.Enquiry, e.Id, e.Code, e.Wanted ?? e.PropertyTitle ?? e.ProjectName ?? "—", e.CloseNote,
                    GetCustomersHandler.Utc(e.ClosedAt), e.AgentId, null, e.CloseReason)))
            .OrderByDescending(u => u.ClosedAt).ToList();

        var deals = dealRows is null ? null : await dealMapper.ManyAsync(dealRows, ct);
        var tenancyDtos = tenancyRows is null ? null : await tenancyMapper.ManyAsync(tenancyRows, ct);
        var ownedDtos = owned?.SelectMany(p => p.Owners.Where(o => partyIds.Contains(o.PartyId))
            .Select(o => new CustomerOwnedPropertyDto(p.Id, p.Code, p.Title, p.Purpose, p.Status, o.PartyId, o.SharePercent, o.Holding))).ToList();

        var summary = CustomerBook.Summarise(id, rows, now);
        var seen = all ? summary?.Totals : summary?.Agents.FirstOrDefault(a => a.AgentId == me);
        var numbers = new EstateCustomerNumbersDto(
            enquiryRows.Count, enquiryRows.Count(e => e.Status != EnquiryStatus.Closed),
            requirementRows.Count(CustomerRules.IsOpen), requirementRows.Count(r => !CustomerRules.IsOpen(r)),
            new CustomerMatchCountsDto(matchRows.Count(m => m.Status == MatchStatus.New), matchRows.Count(m => m.Status == MatchStatus.Shown),
                matchRows.Count(m => m.Status == MatchStatus.Interested), matchRows.Count(m => m.Status == MatchStatus.Rejected)),
            shareRows.Count, shareRows.Count(s => s.Opens > 0),
            visitRows.Count(v => v.Status == VisitStatus.Done), visitRows.Count(v => v.Status == VisitStatus.Scheduled && v.At >= now),
            dealRows?.Count,
            dealRows?.GroupBy(d => d.Stage).OrderBy(g => g.Key).Select(g => new CustomerStageCountDto(g.Key, g.Count())).ToList(),
            tenancyRows?.Count, owned?.Count, unmet.Count);

        return new EstateCustomerDashboardDto(id.ContactId, id.LeadIds, summary?.Name, summary?.Phone ?? (scope.SeesOwners ? summary?.PartyPhone : null), highlighted,
            GetCustomersHandler.Utc(seen?.LastActivityAt), numbers,
            [.. enquiryRows.Select(EnquiryMapping.ToDto)],
            [.. requirementDtos.Select(r => new CustomerRequirementDto(r, matchTotals.GetValueOrDefault(r.Id),
                CustomerRules.Summary(requirementRows.First(x => x.Id == r.Id))))],
            await MatchMapping.ManyAsync(shownMatches, properties, requirements, ct),
            await SharesAsync(shareRows, ct),
            await visitMapper.ManyAsync(visitRows, ct),
            deals, tenancyDtos, ownedDtos, unmet);
    }

    private async Task<IReadOnlyList<CustomerShareDto>> SharesAsync(IReadOnlyList<ShareDocument> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];
        var propertyIds = rows.Select(s => s.PropertyId).OfType<string>().Distinct().ToList();
        var projectIds = rows.Select(s => s.ProjectId).OfType<string>().Distinct().ToList();
        var props = (await properties.GetDocuments(p => propertyIds.Contains(p.Id), true).Select(p => new { p.Id, p.Code, p.Title }).ToListAsync(ct))
            .ToDictionary(p => p.Id);
        var projs = projectIds.Count == 0
            ? new Dictionary<string, string>()
            : (await projects.GetDocuments(p => projectIds.Contains(p.Id), true).Select(p => new { p.Id, p.Name }).ToListAsync(ct)).ToDictionary(p => p.Id, p => p.Name);
        var baseUrl = options.BaseUrl.TrimEnd('/');
        return [.. rows.Select(s =>
        {
            var p = s.PropertyId is { } pid ? props.GetValueOrDefault(pid) : null;
            return new CustomerShareDto(s.Id, s.PropertyId is not null ? $"{baseUrl}/p/{s.Token}" : $"{baseUrl}/pj/{s.Token}", s.PropertyId, p?.Code, p?.Title,
                s.ProjectId, s.ProjectId is { } pj ? projs.GetValueOrDefault(pj) : null, s.RequirementId, s.Opens, GetCustomersHandler.Utc(s.LastOpenedAt),
                new DateTimeOffset(DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc)), s.Enquiries);
        })];
    }
}
