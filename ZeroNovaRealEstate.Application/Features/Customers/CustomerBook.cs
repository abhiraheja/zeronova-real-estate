using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Base;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Application.Features.Customers;

/// <summary>
/// A customer as Real Estate knows them (D32): the zeronova-lead contact, and the leads it has (from our rows, or the ones
/// the app read from <c>ZeroNova_contactLeads</c>). Without a known contact, one lead stands for the customer.
/// </summary>
public sealed record CustomerIdentity(string? ContactId, IReadOnlyList<string> LeadIds)
{
    public const string LeadKeyPrefix = "lead:";

    public string Key => ContactId ?? LeadKeyPrefix + LeadIds[0];
}

/// <summary>Every Real Estate row of one customer, unscoped (the callers filter by permission).</summary>
public sealed class CustomerRows
{
    public List<EnquiryDocument> Enquiries { get; init; } = [];
    public List<RequirementDocument> Requirements { get; init; } = [];
    public List<VisitDocument> Visits { get; init; } = [];
    public List<PartyDocument> Parties { get; init; } = [];
    public List<DealDocument> Deals { get; init; } = [];
    public List<TenancyDocument> Tenancies { get; init; } = [];

    public bool IsEmpty => Enquiries.Count + Requirements.Count + Visits.Count + Parties.Count + Deals.Count + Tenancies.Count == 0;
}

/// <summary>
/// Keeps the Customers list (<c>re_customers</c>, C2) in step with the rows: each write of an enquiry, requirement, visit,
/// deal, tenancy or party calls <see cref="TouchAsync"/> (or a sibling), which resolves the customer, reads their rows and
/// replaces the summary whole — idempotent, so a lost or doubled touch is put right by the next one. A failure is logged
/// and never fails the write. A company's first list builds every summary from the rows (<see cref="EnsureBuiltAsync"/>).
/// Also the contact link (C2): a lead's contact as our rows know it, and the backfill when an enquiry names it.
/// </summary>
public sealed class CustomerBook(
    IEnquiryRepository enquiries, IRequirementRepository requirements, IVisitRepository visits, IPartyRepository parties, IDealRepository deals,
    ITenancyRepository tenancies, ICustomerRepository customers, ICounterRepository counters, TimeProvider clock, ILogger<CustomerBook> logger)
{
    /// <summary>The counter that says a company's summaries were built from its rows (bump the version to build them again).</summary>
    public const string BuiltMarker = "customers-built-v1";

    // ── Contact links ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The lead's contact as our rows know it (an enquiry from zeronova-lead names it), or null.</summary>
    public async Task<string?> ContactOfLeadAsync(string? leadId, CancellationToken ct, string? companyId = null)
    {
        if (EstateText.Clean(leadId) is not { } lead)
            return null;
        return await enquiries.GetDocuments(e => e.LeadId == lead && e.ContactId != null, false, companyId)
                   .OrderByDescending(e => e.OccurredAt).Select(e => e.ContactId).FirstOrDefaultAsync(ct)
               ?? await requirements.GetDocuments(r => r.LeadId == lead && r.ContactId != null, false, companyId).Select(r => r.ContactId).FirstOrDefaultAsync(ct)
               ?? await visits.GetDocuments(v => v.LeadId == lead && v.ContactId != null, false, companyId).Select(v => v.ContactId).FirstOrDefaultAsync(ct)
               ?? await parties.GetDocuments(p => p.LeadId == lead && p.ContactId != null, false, companyId).Select(p => p.ContactId).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// An enquiry named the lead's contact: the lead's requirements, visits and parties that have none take it (cheap and
    /// idempotent: only rows without one change).
    /// </summary>
    public async Task BackfillAsync(string leadId, string contactId, CancellationToken ct, string? companyId = null)
    {
        await requirements.BackfillContactAsync(leadId, contactId, ct, companyId);
        await visits.BackfillContactAsync(leadId, contactId, ct, companyId);
        await parties.BackfillContactAsync(leadId, contactId, ct, companyId);
    }

    /// <summary>
    /// The customer of a contact and / or leads: the contact (else the first lead's, as our rows know it) and every lead our
    /// rows have for that contact, with the ones given. Null when neither is given.
    /// </summary>
    public async Task<CustomerIdentity?> ResolveAsync(string? contactId, IEnumerable<string?>? leadIds, CancellationToken ct, string? companyId = null)
    {
        var leads = (leadIds ?? []).Select(EstateText.Clean).OfType<string>().Distinct().ToList();
        var contact = EstateText.Clean(contactId);
        foreach (var lead in leads)
        {
            if (contact is not null)
                break;
            contact = await ContactOfLeadAsync(lead, ct, companyId);
        }
        if (contact is not null)
        {
            var c = contact;
            leads.AddRange((await enquiries.GetDocuments(e => e.ContactId == c && e.LeadId != null, false, companyId).Select(e => e.LeadId).ToListAsync(ct)).OfType<string>());
            leads.AddRange(await requirements.GetDocuments(r => r.ContactId == c, false, companyId).Select(r => r.LeadId).ToListAsync(ct));
            leads.AddRange(await visits.GetDocuments(v => v.ContactId == c, false, companyId).Select(v => v.LeadId).ToListAsync(ct));
            leads.AddRange((await parties.GetDocuments(p => p.ContactId == c && p.LeadId != null, false, companyId).Select(p => p.LeadId).ToListAsync(ct)).OfType<string>());
        }
        leads = [.. leads.Distinct()];
        return contact is null && leads.Count == 0 ? null : new CustomerIdentity(contact, leads);
    }

    /// <summary>The customer's rows: by contact or lead; parties so; deals by lead, requirement or party; tenancies by party.</summary>
    public async Task<CustomerRows> LoadAsync(CustomerIdentity id, CancellationToken ct, string? companyId = null)
    {
        var c = id.ContactId;
        var leads = id.LeadIds.ToList();
        var enquiryRows = await (c is null
            ? enquiries.GetDocuments(e => leads.Contains(e.LeadId!), false, companyId)
            : enquiries.GetDocuments(e => e.ContactId == c || leads.Contains(e.LeadId!), false, companyId)).ToListAsync(ct);
        var requirementRows = await (c is null
            ? requirements.GetDocuments(r => leads.Contains(r.LeadId), false, companyId)
            : requirements.GetDocuments(r => r.ContactId == c || leads.Contains(r.LeadId), false, companyId)).ToListAsync(ct);
        var visitRows = await (c is null
            ? visits.GetDocuments(v => leads.Contains(v.LeadId), false, companyId)
            : visits.GetDocuments(v => v.ContactId == c || leads.Contains(v.LeadId), false, companyId)).ToListAsync(ct);
        var partyRows = await (c is null
            ? parties.GetDocuments(p => leads.Contains(p.LeadId!), false, companyId)
            : parties.GetDocuments(p => p.ContactId == c || leads.Contains(p.LeadId!), false, companyId)).ToListAsync(ct);
        var partyIds = partyRows.Select(p => p.Id).ToList();
        var requirementIds = requirementRows.Select(r => r.Id).ToList();
        var dealRows = await deals.GetDocuments(d => leads.Contains(d.LeadId!) || requirementIds.Contains(d.RequirementId!)
            || d.BuyerIds.Any(b => partyIds.Contains(b)) || d.SellerIds.Any(s => partyIds.Contains(s)), false, companyId).ToListAsync(ct);
        var tenancyRows = partyIds.Count == 0
            ? []
            : await tenancies.GetDocuments(t => t.TenantIds.Any(x => partyIds.Contains(x)) || t.LandlordIds.Any(x => partyIds.Contains(x)), false, companyId)
                .ToListAsync(ct);
        return new CustomerRows
        {
            Enquiries = enquiryRows, Requirements = requirementRows, Visits = visitRows, Parties = partyRows, Deals = dealRows, Tenancies = tenancyRows,
        };
    }

    // ── Keeping the summaries ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Refreshes the customer of this contact and / or leads; a failure is logged, never thrown.</summary>
    public async Task TouchAsync(string? contactId, IEnumerable<string?> leadIds, CancellationToken ct, string? companyId = null)
    {
        try
        {
            await RefreshAsync(contactId, leadIds, ct, companyId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The customer summary of contact {ContactId} was not refreshed.", contactId);
        }
    }

    /// <summary>The customers of these parties (each by its contact or lead).</summary>
    public async Task TouchPartiesAsync(IEnumerable<string> partyIds, CancellationToken ct, string? companyId = null)
    {
        var ids = partyIds.Distinct().ToList();
        if (ids.Count == 0)
            return;
        try
        {
            var linked = await parties.GetDocuments(p => ids.Contains(p.Id) && (p.ContactId != null || p.LeadId != null), true, companyId)
                .Select(p => new { p.ContactId, p.LeadId }).ToListAsync(ct);
            foreach (var who in linked.Distinct())
                await TouchAsync(who.ContactId, [who.LeadId], ct, companyId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The customer summaries of {Count} parties were not refreshed.", ids.Count);
        }
    }

    /// <summary>The customers of a deal: its lead, its requirement's customer, its sellers and buyers.</summary>
    public async Task TouchDealAsync(DealDocument deal, CancellationToken ct)
    {
        if (deal.LeadId is not null)
            await TouchAsync(null, [deal.LeadId], ct);
        if (deal.RequirementId is { } requirementId)
        {
            var requirement = await requirements.GetDocuments(r => r.Id == requirementId, true).Select(r => new { r.ContactId, r.LeadId }).FirstOrDefaultAsync(ct);
            if (requirement is not null)
                await TouchAsync(requirement.ContactId, [requirement.LeadId], ct);
        }
        await TouchPartiesAsync(deal.SellerIds.Concat(deal.BuyerIds), ct);
    }

    public async Task RefreshAsync(string? contactId, IEnumerable<string?> leadIds, CancellationToken ct, string? companyId = null)
    {
        if (await ResolveAsync(contactId, leadIds, ct, companyId) is not { } id)
            return;
        var summary = Summarise(id, await LoadAsync(id, ct, companyId), clock.GetUtcNow().UtcDateTime);
        // A lead's own row is merged into its contact's (or into the one lead that stands for the customer).
        var merged = id.LeadIds.Select(l => CustomerIdentity.LeadKeyPrefix + l).Where(k => k != id.Key).ToList();
        if (summary is null)
            merged.Add(id.Key);
        else
            await customers.UpsertAsync(summary, ct, companyId);
        await customers.RemoveKeysAsync(merged, ct, companyId);
    }

    /// <summary>Builds the company's summaries from its rows once (the first list after C2 is deployed).</summary>
    public async Task EnsureBuiltAsync(CancellationToken ct)
    {
        if (await counters.GetDocuments(c => c.Name == BuiltMarker).AnyAsync(ct))
            return;
        await RebuildAsync(ct);
        await counters.NextAsync(BuiltMarker, ct);
    }

    /// <summary>
    /// Every summary of the company from its rows, in one pass: every contact our rows name (with its leads), then every lead
    /// no row ties to a contact. A summary no customer has any more is removed. The number of customers.
    /// </summary>
    public async Task<int> RebuildAsync(CancellationToken ct, string? companyId = null)
    {
        var all = new CustomerRows
        {
            Enquiries = await enquiries.GetDocuments(e => e.LeadId != null || e.ContactId != null, false, companyId).ToListAsync(ct),
            Requirements = await requirements.GetDocuments(false, companyId).ToListAsync(ct),
            Visits = await visits.GetDocuments(false, companyId).ToListAsync(ct),
            Parties = await parties.GetDocuments(p => p.LeadId != null || p.ContactId != null, false, companyId).ToListAsync(ct),
            Deals = await deals.GetDocuments(false, companyId).ToListAsync(ct),
            Tenancies = await tenancies.GetDocuments(false, companyId).ToListAsync(ct),
        };

        var contactOfLead = new Dictionary<string, string>();
        foreach (var (lead, contact) in all.Enquiries.OrderByDescending(e => e.OccurredAt).Select(e => (e.LeadId, e.ContactId))
                     .Concat(all.Requirements.Select(r => ((string?)r.LeadId, r.ContactId)))
                     .Concat(all.Visits.Select(v => ((string?)v.LeadId, v.ContactId)))
                     .Concat(all.Parties.Select(p => (p.LeadId, p.ContactId))))
            if (lead is not null && contact is not null)
                contactOfLead.TryAdd(lead, contact);

        var allLeads = all.Enquiries.Select(e => e.LeadId).Concat(all.Requirements.Select(r => (string?)r.LeadId)).Concat(all.Visits.Select(v => (string?)v.LeadId))
            .Concat(all.Parties.Select(p => p.LeadId)).Concat(all.Deals.Select(d => d.LeadId)).OfType<string>().Distinct().ToList();
        var contacts = all.Enquiries.Select(e => e.ContactId).Concat(all.Requirements.Select(r => r.ContactId)).Concat(all.Visits.Select(v => v.ContactId))
            .Concat(all.Parties.Select(p => p.ContactId)).Concat(contactOfLead.Values).OfType<string>().Distinct().ToList();

        var identities = contacts.Select(c => new CustomerIdentity(c, [.. allLeads.Where(l => contactOfLead.GetValueOrDefault(l) == c)
                .Concat(all.Enquiries.Where(e => e.ContactId == c).Select(e => e.LeadId).OfType<string>())
                .Concat(all.Requirements.Where(r => r.ContactId == c).Select(r => r.LeadId))
                .Concat(all.Visits.Where(v => v.ContactId == c).Select(v => v.LeadId))
                .Concat(all.Parties.Where(p => p.ContactId == c).Select(p => p.LeadId).OfType<string>()).Distinct()]))
            .Concat(allLeads.Where(l => !contactOfLead.ContainsKey(l)).Select(l => new CustomerIdentity(null, [l])))
            .ToList();

        var now = clock.GetUtcNow().UtcDateTime;
        var keys = new HashSet<string>();
        foreach (var id in identities)
        {
            if (Summarise(id, Select(id, all), now) is not { } summary || !keys.Add(summary.Key))
                continue;
            await customers.UpsertAsync(summary, ct, companyId);
        }
        var stale = (await customers.GetDocuments(false, companyId).Select(c => c.Key).ToListAsync(ct)).Where(k => !keys.Contains(k)).ToList();
        await customers.RemoveKeysAsync(stale, ct, companyId);
        return keys.Count;
    }

    /// <summary>The rows of one customer out of the company's (the same rules as <see cref="LoadAsync"/>).</summary>
    private static CustomerRows Select(CustomerIdentity id, CustomerRows all)
    {
        var c = id.ContactId;
        var leads = id.LeadIds.ToHashSet();
        bool Mine(string? contact, string? lead) => (c is not null && contact == c) || (lead is not null && leads.Contains(lead));
        var partyRows = all.Parties.Where(p => Mine(p.ContactId, p.LeadId)).ToList();
        var partyIds = partyRows.Select(p => p.Id).ToHashSet();
        var requirementRows = all.Requirements.Where(r => Mine(r.ContactId, r.LeadId)).ToList();
        var requirementIds = requirementRows.Select(r => r.Id).ToHashSet();
        return new CustomerRows
        {
            Enquiries = [.. all.Enquiries.Where(e => Mine(e.ContactId, e.LeadId))],
            Requirements = requirementRows,
            Visits = [.. all.Visits.Where(v => Mine(v.ContactId, v.LeadId))],
            Parties = partyRows,
            Deals = [.. all.Deals.Where(d => (d.LeadId is not null && leads.Contains(d.LeadId)) || (d.RequirementId is not null && requirementIds.Contains(d.RequirementId))
                || d.BuyerIds.Any(partyIds.Contains) || d.SellerIds.Any(partyIds.Contains))],
            Tenancies = [.. all.Tenancies.Where(t => t.TenantIds.Any(partyIds.Contains) || t.LandlordIds.Any(partyIds.Contains))],
        };
    }

    // ── The summary ──────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Touch(string? AgentId, DateTime? At, string? Name, string? Phone);

    /// <summary>
    /// The list's row from the customer's rows: name and phone as the latest enquiry, requirement or visit has them (else a
    /// party's), the agent of the latest row, the counts in all and per agent, and the last activity (a row's last change).
    /// Null when the customer has no row left.
    /// </summary>
    public static CustomerDocument? Summarise(CustomerIdentity id, CustomerRows rows, DateTime now)
    {
        if (rows.IsEmpty || (id.ContactId is null && id.LeadIds.Count == 0))
            return null;
        var touches = rows.Enquiries.Select(e => new Touch(e.AgentId, Max(e.OccurredAt, e.UpdatedAt), e.Name, e.Phone))
            .Concat(rows.Requirements.Select(r => new Touch(r.AgentId, Changed(r), r.LeadName, r.LeadPhone)))
            .Concat(rows.Visits.Select(v => new Touch(v.AgentId, Changed(v), v.LeadName, v.LeadPhone)))
            .Concat(rows.Deals.Select(d => new Touch(d.AgentId, Changed(d), null, null)))
            .Concat(rows.Tenancies.Select(t => new Touch(t.AgentId, Changed(t), null, null)))
            .OrderByDescending(t => t.At ?? DateTime.MinValue).ToList();
        var latestParty = rows.Parties.OrderByDescending(p => p.Id).FirstOrDefault();
        var agent = touches.FirstOrDefault(t => t.AgentId is not null)?.AgentId;

        var agents = touches.Select(t => t.AgentId).OfType<string>().Distinct().Order().ToList();
        var summary = new CustomerDocument
        {
            Id = string.Empty,
            Key = id.Key,
            ContactId = id.ContactId,
            LeadIds = [.. id.LeadIds],
            RequirementIds = [.. rows.Requirements.Select(r => r.Id)],
            PartyIds = [.. rows.Parties.Select(p => p.Id)],
            Name = touches.FirstOrDefault(t => t.Name is not null)?.Name ?? latestParty?.Name,
            Phone = touches.FirstOrDefault(t => t.Phone is not null)?.Phone,
            AgentId = agent,
            AgentName = agent is null ? null : rows.Enquiries.OrderByDescending(e => e.OccurredAt).FirstOrDefault(e => e.AgentId == agent && e.AgentName != null)?.AgentName,
            AgentIds = agents,
            Totals = Counts(null, rows, touches),
            Agents = [.. agents.Select(a => Counts(a, rows, touches))],
            LastActivityAt = touches.FirstOrDefault()?.At,
            RefreshedAt = now,
        };
        if (summary.Phone is null)
            summary.PartyPhone = rows.Parties.OrderByDescending(p => p.Id).Select(p => p.Phone ?? p.WhatsApp).FirstOrDefault(p => p is not null);
        summary.SearchText = string.Join(' ', touches.Select(t => t.Name).Concat(rows.Parties.Select(p => p.Name))
                .Concat(touches.Select(t => t.Phone)).Concat(rows.Parties.Select(p => p.Phone)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            .ToLowerInvariant();
        return summary;
    }

    private static CustomerCountsModel Counts(string? agent, CustomerRows rows, IReadOnlyList<Touch> touches)
    {
        bool Of(string? rowAgent) => agent is null || rowAgent == agent;
        return new CustomerCountsModel
        {
            AgentId = agent,
            Enquiries = rows.Enquiries.Count(e => Of(e.AgentId)),
            OpenRequirements = rows.Requirements.Count(r => Of(r.AgentId) && CustomerRules.IsOpen(r)),
            Visits = rows.Visits.Count(v => Of(v.AgentId)),
            Deals = rows.Deals.Count(d => Of(d.AgentId)),
            Tenancies = rows.Tenancies.Count(t => Of(t.AgentId)),
            Unmet = rows.Requirements.Count(r => Of(r.AgentId) && CustomerRules.IsUnmet(r)) + rows.Enquiries.Count(e => Of(e.AgentId) && CustomerRules.IsUnmet(e, rows.Requirements)),
            LastActivityAt = touches.Where(t => Of(t.AgentId)).Select(t => t.At).FirstOrDefault(),
        };
    }

    private static DateTime? Changed(DocumentBase d) => (d.UpdatedBy?.ChangedDate ?? d.CreatedBy?.ChangedDate)?.UtcDateTime;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}

/// <summary>What counts as open and as unmet demand (D32, C3).</summary>
public static partial class CustomerRules
{
    public static bool IsOpen(RequirementDocument r) => r.Status is RequirementStatus.Active or RequirementStatus.OnHold;

    /// <summary>
    /// A requirement watched (D33), lost, or closed as not available — by its reason code (C3), or, on a row closed before C3
    /// (no code), by its note saying so ("not available", "nahi mila"); one closed by a deal is met.
    /// </summary>
    public static bool IsUnmet(RequirementDocument r) =>
        r.Status is RequirementStatus.Watching or RequirementStatus.Lost || (r.Status == RequirementStatus.Closed && IsNotAvailable(r));

    /// <summary>Closed as not available: the code, or — a row from before C3 with no code — the note's words.</summary>
    public static bool IsNotAvailable(RequirementDocument r) =>
        r.CloseReasonCode == RequirementCloseReason.NotAvailable || (r.CloseReasonCode is null && r.CloseReason is { } reason && NotAvailable().IsMatch(reason));

    /// <summary>
    /// An enquiry closed as not available — unless it made a watched requirement (D33): then the need is that requirement's,
    /// counted once.
    /// </summary>
    public static bool IsUnmet(EnquiryDocument e, IEnumerable<RequirementDocument> requirements) =>
        e.Status == EnquiryStatus.Closed && e.CloseReason == EnquiryCloseReason.NotAvailable && !requirements.Any(r => r.EnquiryId == e.Id);

    /// <summary>"3 BHK flat for sale · Sector 70 / Sector 71, Mohali · ₹80,00,000 – ₹1,00,00,000 · 1,500 sq ft or more".</summary>
    public static string Summary(RequirementDocument r)
    {
        var bhk = r.Bhk.Count switch
        {
            0 => null,
            1 => $"{r.Bhk[0]} BHK",
            _ when r.Bhk[^1] - r.Bhk[0] == r.Bhk.Count - 1 => $"{r.Bhk[0]}–{r.Bhk[^1]} BHK",
            _ => string.Join(", ", r.Bhk) + " BHK",
        };
        var types = r.Types.Count == 0 ? "property" : string.Join(" / ", r.Types.Select(Words));
        var what = string.Join(' ', new[] { bhk, types, r.Purpose == PropertyPurpose.Rent ? "for rent" : "for sale" }.OfType<string>());
        var where = string.Join(", ", new[] { r.Localities.Count == 0 ? null : string.Join(" / ", r.Localities), r.City }.OfType<string>());
        var budget = (r.BudgetMin, r.BudgetMax) switch
        {
            ({ } min, { } max) => $"{EstateText.Rupees(min)} – {EstateText.Rupees(max)}",
            (null, { } max) => $"up to {EstateText.Rupees(max)}",
            ({ } min, null) => $"from {EstateText.Rupees(min)}",
            _ => null,
        };
        var size = (r.SizeMin, r.SizeMax) switch
        {
            ({ } min, { } max) => $"{AreaMath.Label(min)} – {AreaMath.Label(max)}",
            ({ } min, null) => $"{AreaMath.Label(min)} or more",
            (null, { } max) => $"up to {AreaMath.Label(max)}",
            _ => null,
        };
        return string.Join(" · ", new[] { what, where.Length == 0 ? null : where, budget, size }.OfType<string>());
    }

    private static string Words(PropertyType type) => Camel().Replace(type.ToString(), " $1").Trim().ToLowerInvariant();

    [GeneratedRegex(@"not\s*available|nahi\s*mila|mila\s*nahi", RegexOptions.IgnoreCase)]
    private static partial Regex NotAvailable();

    [GeneratedRegex("([A-Z])")]
    private static partial Regex Camel();
}
