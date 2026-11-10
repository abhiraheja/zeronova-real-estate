using System.Globalization;
using FluentValidation;
using MongoDB.Bson;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Configuration;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Events;

namespace ZeroNovaRealEstate.Application.Features.Enquiries;

// ── The share page's "Enquire" form (anonymous, D29) ──────────────────────────────────────────────────────

/// <summary>
/// The share page's "Enquire" (<c>/p/:token</c>, <c>/pj/:token</c>): name, phone, message. Sent to zeronova-lead as
/// <c>lead-captured-enquiry</c> — the lead is found or made there, and the enquiry comes back here on
/// <c>lead-enquiry-received</c>. A share made for a requirement gives a new lead to the requirement's agent. At most
/// <see cref="ShareOptions.EnquiriesPerHour"/> per link in any hour.
/// </summary>
public sealed record EnquireShareCommand(string Token, string Name, string Phone, string? Message) : IRequest<ShareEnquiryAckDto>;

public sealed class EnquireShareValidator : AbstractValidator<EnquireShareCommand>
{
    public EnquireShareValidator()
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("This link is not valid.").MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().WithMessage("Please give your name.").MaximumLength(100);
        RuleFor(x => x.Phone).Must(p => IndianMobile.Normalize(p) is not null).WithMessage("Please give a 10-digit Indian mobile number.");
        RuleFor(x => x.Message).MaximumLength(1000);
    }
}

public sealed class EnquireShareHandler(IShareRepository shares, IRequirementRepository requirements, ILeadCaptureProducer captures, ShareOptions options,
    TimeProvider clock)
    : IRequestHandler<EnquireShareCommand, ShareEnquiryAckDto>
{
    public async Task<ShareEnquiryAckDto> Handle(EnquireShareCommand request, CancellationToken ct)
    {
        var share = await shares.FindByTokenAnyTenantAsync(request.Token.Trim(), ct) ?? throw new ValidatorException("This link is not valid.");
        if (share.PropertyId is null && share.ProjectId is null)
            throw new ValidatorException("This link is not valid.");
        var company = share.CompanyId;
        var requirement = share.RequirementId is { } rid ? await requirements.GetDocument(rid, true, company).FirstOrDefaultAsync(ct) : null;

        var now = clock.GetUtcNow();
        if (!await shares.TryRecordEnquiryAsync(share.Id, company, now.UtcDateTime, options.EnquiriesPerHour, ct))
            throw new ValidatorException("We have your enquiry already. The dealer will call you soon — or call them directly.");

        var metadata = new Dictionary<string, string>
        {
            [EnquiryMetadata.Product] = EnquiryMetadata.ProductRealEstate,
            [EnquiryMetadata.Estate] = "1",
            [EnquiryMetadata.Form] = EnquiryMetadata.SharePageForm,
            [EnquiryMetadata.ShareToken] = share.Token,
        };
        if (share.PropertyId is { } propertyId)
            metadata[EnquiryMetadata.PropertyId] = propertyId;
        else
            metadata[EnquiryMetadata.ProjectId] = share.ProjectId!;
        if (requirement is not null)
        {
            metadata[EnquiryMetadata.RequirementId] = requirement.Id;
            metadata[EnquiryMetadata.AgentId] = requirement.AgentId;
        }

        var url = $"{options.BaseUrl.TrimEnd('/')}/{(share.PropertyId is not null ? "p" : "pj")}/{share.Token}";
        await captures.PublishAsync(new LeadCapturedEnquiryEvent(company, LeadSources.Website, ActivityChannels.Website, EstateText.Clean(request.Name),
            IndianMobile.Normalize(request.Phone), null, request.Message?.Trim() is { Length: > 0 } m ? m : null, metadata, url, EnquiryRefs.New(), now,
            requirement?.AgentId, null, null), "share-page", ct);
        return new ShareEnquiryAckDto(true, "Thank you. The dealer will call you soon.");
    }
}

// ── Staff: "Add enquiry" (D29) ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// "Add enquiry": a call, walk-in, WhatsApp or other enquiry typed in by staff — for a lead picked, or a new person by
/// name and phone. Sent to zeronova-lead (which finds or makes the lead); the row appears when <c>lead-enquiry-received</c>
/// comes back. The answer's ref is what <c>ZeroNova_estateEnquiryByRef</c> polls.
/// </summary>
public sealed record AddEnquiryCommand(AddEnquiryInput Input) : IRequest<EnquiryAckDto>;

public sealed class AddEnquiryValidator : AbstractValidator<AddEnquiryCommand>
{
    public static readonly EnquirySource[] StaffSources = [EnquirySource.Call, EnquirySource.WalkIn, EnquirySource.WhatsApp, EnquirySource.Other];

    public AddEnquiryValidator()
    {
        RuleFor(x => x.Input).Must(i => !string.IsNullOrWhiteSpace(i.LeadId) || (!string.IsNullOrWhiteSpace(i.Name) && !string.IsNullOrWhiteSpace(i.Phone)))
            .WithMessage("Pick the customer (lead), or give a name and phone.");
        RuleFor(x => x.Input.Phone).Must(p => IndianMobile.Normalize(p) is not null).When(x => !string.IsNullOrWhiteSpace(x.Input.Phone))
            .WithMessage("Enter a 10-digit Indian mobile number.");
        RuleFor(x => x.Input.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Input.Email)).WithMessage("The email is not valid.");
        RuleFor(x => x.Input.Name).MaximumLength(100);
        RuleFor(x => x.Input.Message).MaximumLength(2000);
        RuleFor(x => x.Input.Source).Must(s => StaffSources.Contains(s)).WithMessage("An enquiry added by staff is a call, walk-in, WhatsApp or other.");
    }
}

public sealed class AddEnquiryHandler(IPropertyRepository properties, IProjectRepository projects, IUnitTypeRepository unitTypes, ILeadCaptureProducer captures,
    IRequestContext context, EstateScope scope, TimeProvider clock)
    : IRequestHandler<AddEnquiryCommand, EnquiryAckDto>
{
    public async Task<EnquiryAckDto> Handle(AddEnquiryCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var me = scope.UserId;
        var agent = EstateText.Clean(input.AgentId) ?? me;
        if (agent != me && !scope.SeesAllRequirements)
            throw new ValidatorException("Only a manager can give an enquiry to another agent.");

        var listing = await StaffListingAsync(input, ct);
        var metadata = new Dictionary<string, string>
        {
            [EnquiryMetadata.Product] = EnquiryMetadata.ProductRealEstate,
            [EnquiryMetadata.Estate] = "1",
            [EnquiryMetadata.Source] = input.Source.ToString(),
            [EnquiryMetadata.AgentId] = agent,
        };
        if (EstateText.Clean(input.AgentName) is { } agentName)
            metadata[EnquiryMetadata.AgentName] = agentName;
        if (listing.Property is not null)
            metadata[EnquiryMetadata.PropertyId] = listing.Property.Id;
        if (listing.Project is not null)
            metadata[EnquiryMetadata.ProjectId] = listing.Project.Id;
        if (listing.UnitTypeId is not null)
            metadata[EnquiryMetadata.UnitTypeId] = listing.UnitTypeId;

        var (leadSource, channel) = input.Source switch
        {
            EnquirySource.Call => (LeadSources.ManualCall, ActivityChannels.Call),
            EnquirySource.WhatsApp => (LeadSources.WhatsApp, ActivityChannels.WhatsApp),
            _ => (LeadSources.Manual, ActivityChannels.Internal),
        };
        var reference = EnquiryRefs.New();
        var now = clock.GetUtcNow();
        await captures.PublishAsync(new LeadCapturedEnquiryEvent(context.CompanyId ?? throw new ValidatorException("No active company. Select a company first."),
            leadSource, channel, EstateText.Clean(input.Name), IndianMobile.Normalize(input.Phone), EstateText.Clean(input.Email),
            input.Message?.Trim() is { Length: > 0 } m ? m : null, metadata, null, reference, now, agent, EstateText.Clean(input.AgentName),
            EstateText.Clean(input.LeadId)), me, ct);
        return new EnquiryAckDto(reference, now);
    }

    /// <summary>The property, project and unit type picked — each of this company, and of one another.</summary>
    private async Task<EnquiryListing> StaffListingAsync(AddEnquiryInput input, CancellationToken ct)
    {
        foreach (var id in new[] { input.PropertyId, input.ProjectId, input.UnitTypeId })
            if (EstateText.Clean(id) is { } x && !ObjectId.TryParse(x, out _))
                throw new ValidatorException("The property, project or unit type was not found.");
        var listing = await EnquiryListings.ResolveAsync(null, input.PropertyId, input.ProjectId, input.UnitTypeId, properties, projects, unitTypes, ct);
        if (listing.PropertyRef is not null && listing.Property is not { IsDeleted: false })
            throw new ValidatorException("Property not found.");
        if (listing.ProjectRef is not null && listing.Project is not { IsDeleted: false })
            throw new ValidatorException("Project not found.");
        if (listing.Property is { ProjectId: { } unitOf } && listing.ProjectRef is { } asked && asked != unitOf)
            throw new ValidatorException($"{listing.Property.Code} is not a unit of that project.");
        if (EstateText.Clean(input.UnitTypeId) is { } typeId && listing.UnitTypeId != typeId)
            throw new ValidatorException(listing.Project is null ? "Pick the project of the unit type." : "That unit type is not of this project.");
        return listing;
    }
}

// ── Working an enquiry (D31) ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A status change, a close with its reason (not available keeps what was wanted — C3 watches it), a requirement or visit
/// linked, or (a manager) another agent. Closing never touches the lead: it keeps its own status in zeronova-lead.
/// </summary>
public sealed record UpdateEnquiryCommand(string EnquiryId, UpdateEnquiryInput Input) : IRequest<EnquiryDto>;

public sealed class UpdateEnquiryValidator : AbstractValidator<UpdateEnquiryCommand>
{
    public UpdateEnquiryValidator()
    {
        RuleFor(x => x.Input.Wanted).MaximumLength(1000);
        RuleFor(x => x.Input.CloseNote).MaximumLength(1000);
    }
}

public sealed class UpdateEnquiryHandler(IEnquiryRepository enquiries, IRequirementRepository requirements, IVisitRepository visits, EstateScope scope, TimeProvider clock,
    Customers.CustomerBook customers)
    : IRequestHandler<UpdateEnquiryCommand, EnquiryDto>
{
    public async Task<EnquiryDto> Handle(UpdateEnquiryCommand request, CancellationToken ct)
    {
        var e = await EnquiryLookup.RequireAsync(enquiries, scope, request.EnquiryId, ct);
        var input = request.Input;
        var now = clock.GetUtcNow().UtcDateTime;

        if (EstateText.Clean(input.AgentId) is { } agent && agent != e.AgentId)
        {
            if (!scope.SeesAllRequirements)
                throw new ValidatorException("Only a manager can give an enquiry to another agent.");
            e.AgentId = agent;
            e.AgentName = EstateText.Clean(input.AgentName);
        }

        if (EstateText.Clean(input.RequirementId) is { } requirementId)
        {
            var requirement = ObjectId.TryParse(requirementId, out _) ? await requirements.GetDocument(requirementId).FirstOrDefaultAsync(ct) : null;
            if (requirement is null)
                throw new ValidatorException("Requirement not found.");
            scope.RequireWork(requirement);
            EnquiryLinks.RequireSameCustomer(e, requirement.LeadId);
            EnquiryLinks.ToRequirement(e, requirement.Id, now);
        }
        if (EstateText.Clean(input.VisitId) is { } visitId)
        {
            var visit = ObjectId.TryParse(visitId, out _) ? await visits.GetDocument(visitId).FirstOrDefaultAsync(ct) : null;
            if (visit is null || (!scope.SeesAllRequirements && visit.AgentId != scope.UserId))
                throw new ValidatorException("Visit not found.");
            EnquiryLinks.RequireSameCustomer(e, visit.LeadId);
            EnquiryLinks.ToVisit(e, visit.Id, now);
        }

        if (input.Status is { } status)
        {
            if (status == EnquiryStatus.Closed)
            {
                var reason = input.CloseReason ?? throw new ValidatorException("Say why the enquiry is closed.");
                var wanted = EstateText.Clean(input.Wanted);
                if (reason == EnquiryCloseReason.NotAvailable && wanted is null)
                    throw new ValidatorException("Say what was wanted and not available.");
                e.Status = EnquiryStatus.Closed;
                e.CloseReason = reason;
                e.Wanted = reason == EnquiryCloseReason.NotAvailable ? wanted : null;
                e.CloseNote = EstateText.Clean(input.CloseNote);
                e.ClosedAt = now;
            }
            else
            {
                if (status == EnquiryStatus.RequirementAdded && e.RequirementId is null)
                    throw new ValidatorException("Add or link the requirement first.");
                if (status == EnquiryStatus.VisitBooked && e.VisitId is null)
                    throw new ValidatorException("Book or link the visit first.");
                e.Status = status;
                e.CloseReason = null;
                e.Wanted = null;
                e.CloseNote = null;
                e.ClosedAt = null;
                if (status == EnquiryStatus.Contacted)
                    e.ContactedAt ??= now;
            }
        }
        else if (input.CloseReason is not null)
            throw new ValidatorException("A close reason goes with closing the enquiry.");

        e.UpdatedAt = now;
        await enquiries.UpdateAsync(e, ct);
        await customers.TouchAsync(e.ContactId, [e.LeadId], ct);
        return EnquiryMapping.ToDto(e);
    }
}

/// <summary>An enquiry moved on by a requirement or a visit made from it (D31). A closed enquiry only keeps the link.</summary>
public static class EnquiryLinks
{
    public static void RequireSameCustomer(EnquiryDocument e, string leadId)
    {
        if (e.LeadId is { } own && own != leadId)
            throw new ValidatorException("This enquiry is another customer's.");
    }

    public static void ToRequirement(EnquiryDocument e, string requirementId, DateTime now)
    {
        e.RequirementId = requirementId;
        if (e.Status is EnquiryStatus.New or EnquiryStatus.Contacted)
            e.Status = EnquiryStatus.RequirementAdded;
        e.UpdatedAt = now;
    }

    public static void ToVisit(EnquiryDocument e, string visitId, DateTime now)
    {
        e.VisitId = visitId;
        if (e.Status != EnquiryStatus.Closed)
            e.Status = EnquiryStatus.VisitBooked;
        e.UpdatedAt = now;
    }

    /// <summary>The enquiry a requirement or visit is saved with: the caller's to work, and the same customer.</summary>
    public static async Task<EnquiryDocument?> ForLinkAsync(IEnquiryRepository enquiries, EstateScope scope, string? enquiryId, string leadId, CancellationToken ct)
    {
        if (EstateText.Clean(enquiryId) is not { } id)
            return null;
        var e = await EnquiryLookup.RequireAsync(enquiries, scope, id, ct);
        RequireSameCustomer(e, leadId);
        return e;
    }
}

// ── Reading (D30; D39: an agent's own, everyone's with PRQV) ───────────────────────────────────────────────

public sealed record GetEnquiriesQuery(EnquiryFilterInput Filter, int Skip = 0, int Take = 50) : IRequest<EnquiryPageDto>;

public sealed class GetEnquiriesHandler(IEnquiryRepository enquiries, EstateScope scope) : IRequestHandler<GetEnquiriesQuery, EnquiryPageDto>
{
    public async Task<EnquiryPageDto> Handle(GetEnquiriesQuery request, CancellationToken ct)
    {
        var query = EnquiryQueries.Filtered(enquiries, scope, request.Filter, true);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Skip(Math.Max(0, request.Skip)).Take(Math.Clamp(request.Take, 1, 200)).ToListAsync(ct);
        return new EnquiryPageDto(total, [.. rows.Select(EnquiryMapping.ToDto)]);
    }
}

public sealed record GetEnquiryQuery(string EnquiryId) : IRequest<EnquiryDto>;

public sealed class GetEnquiryHandler(IEnquiryRepository enquiries, EstateScope scope) : IRequestHandler<GetEnquiryQuery, EnquiryDto>
{
    public async Task<EnquiryDto> Handle(GetEnquiryQuery request, CancellationToken ct) =>
        EnquiryMapping.ToDto(await EnquiryLookup.RequireAsync(enquiries, scope, request.EnquiryId, ct));
}

/// <summary>The enquiry an "Add enquiry" (or the office) sent, by its ref — null while zeronova-lead is still recording it.</summary>
public sealed record GetEnquiryByRefQuery(string Ref) : IRequest<EnquiryDto?>;

public sealed class GetEnquiryByRefHandler(IEnquiryRepository enquiries, EstateScope scope) : IRequestHandler<GetEnquiryByRefQuery, EnquiryDto?>
{
    public async Task<EnquiryDto?> Handle(GetEnquiryByRefQuery request, CancellationToken ct)
    {
        if (EstateText.Clean(request.Ref) is not { } reference)
            return null;
        var e = await enquiries.GetDocuments(x => x.Ref == reference).FirstOrDefaultAsync(ct);
        if (e is null)
            return null;
        scope.RequireWork(e);
        return EnquiryMapping.ToDto(e);
    }
}

/// <summary>
/// Day / week / month views (D30): India days <paramref name="From"/> to <paramref name="To"/> (yyyy-MM-dd, both included),
/// widened to whole buckets (a week from its Monday, a month from its 1st). Defaults: the last 30 days, 12 weeks or 12
/// months to today. The filter's own from/to are not used here. Open and closed are as the enquiries stand now.
/// </summary>
public sealed record GetEnquiryStatsQuery(EnquiryFilterInput Filter, EnquiryBucket Bucket, string? From, string? To) : IRequest<EnquiryStatsDto>;

public sealed class GetEnquiryStatsHandler(IEnquiryRepository enquiries, EstateScope scope, TimeProvider clock) : IRequestHandler<GetEnquiryStatsQuery, EnquiryStatsDto>
{
    private const int MaxBuckets = 400;

    public async Task<EnquiryStatsDto> Handle(GetEnquiryStatsQuery request, CancellationToken ct)
    {
        var to = Day(request.To, "To") ?? IndiaTime.Today(clock);
        var from = Day(request.From, "From") ?? request.Bucket switch
        {
            EnquiryBucket.Week => to.AddDays(-7 * 11),
            EnquiryBucket.Month => to.AddMonths(-11),
            _ => to.AddDays(-29),
        };
        if (from > to)
            throw new ValidatorException("From must not be after To.");
        from = Start(from, request.Bucket);
        var end = Next(Start(to, request.Bucket), request.Bucket);   // exclusive
        var starts = new List<DateOnly>();
        for (var d = from; d < end; d = Next(d, request.Bucket))
        {
            if (starts.Count == MaxBuckets)
                throw new ValidatorException($"At most {MaxBuckets} {request.Bucket.ToString().ToLowerInvariant()}s at a time.");
            starts.Add(d);
        }

        var fromUtc = IndiaTime.StartUtc(from);
        var endUtc = IndiaTime.StartUtc(end);
        var rows = await EnquiryQueries.Filtered(enquiries, scope, request.Filter, false)
            .Where(e => e.OccurredAt >= fromUtc && e.OccurredAt < endUtc)
            .Select(e => new { e.OccurredAt, e.Source, e.Status }).ToListAsync(ct);

        var byBucket = rows.ToLookup(r => Start(IndiaTime.DateOf(r.OccurredAt), request.Bucket));
        var buckets = starts.Select(s =>
        {
            var inIt = byBucket[s].ToList();
            return new EnquiryBucketDto(IndiaTime.Text(s), inIt.Count, BySource(inIt.Select(r => r.Source)),
                inIt.Count(r => r.Status != EnquiryStatus.Closed), inIt.Count(r => r.Status == EnquiryStatus.Closed));
        }).ToList();
        return new EnquiryStatsDto(request.Bucket, IndiaTime.Text(from), IndiaTime.Text(end.AddDays(-1)), rows.Count, BySource(rows.Select(r => r.Source)),
            rows.Count(r => r.Status != EnquiryStatus.Closed), rows.Count(r => r.Status == EnquiryStatus.Closed), buckets);
    }

    public static IReadOnlyList<EnquirySourceCountDto> BySource(IEnumerable<EnquirySource> sources) =>
        [.. sources.GroupBy(s => s).OrderBy(g => g.Key).Select(g => new EnquirySourceCountDto(g.Key, g.Count()))];

    private static DateOnly? Day(string? value, string field) => EstateText.Date(value, field) is { } d ? DateOnly.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static DateOnly Start(DateOnly day, EnquiryBucket bucket) => bucket switch
    {
        EnquiryBucket.Week => IndiaTime.WeekStart(day),
        EnquiryBucket.Month => IndiaTime.MonthStart(day),
        _ => day,
    };

    private static DateOnly Next(DateOnly start, EnquiryBucket bucket) => bucket switch
    {
        EnquiryBucket.Week => start.AddDays(7),
        EnquiryBucket.Month => start.AddMonths(1),
        _ => start.AddDays(1),
    };
}

// ── Shared ────────────────────────────────────────────────────────────────────────────────────────────────

internal static class EnquiryLookup
{
    public static async Task<EnquiryDocument> RequireAsync(IEnquiryRepository enquiries, EstateScope scope, string id, CancellationToken ct)
    {
        var e = ObjectId.TryParse(id?.Trim(), out _) ? await enquiries.GetDocument(id!.Trim()).FirstOrDefaultAsync(ct) : null;
        if (e is null)
            throw new ValidatorException("Enquiry not found.");
        scope.RequireWork(e);
        return e;
    }
}

public static class EnquiryQueries
{
    /// <summary>The caller's enquiries (their own, or everyone's with PRQV) with the filter; <paramref name="window"/> = apply its from / to.</summary>
    public static IQueryable<EnquiryDocument> Filtered(IEnquiryRepository enquiries, EstateScope scope, EnquiryFilterInput? f, bool window)
    {
        var query = enquiries.GetDocuments();
        if (!scope.SeesAllRequirements)
        {
            var me = scope.UserId;
            query = query.Where(e => e.AgentId == me);
        }
        else if (EstateText.Clean(f?.AgentId) is { } agent)
            query = query.Where(e => e.AgentId == agent);
        if (f is null)
            return query;
        if (f.Source is { } source)
            query = query.Where(e => e.Source == source);
        if (f.Status is { } status)
            query = query.Where(e => e.Status == status);
        if (EstateText.Clean(f.PropertyId) is { } propertyId)
            query = query.Where(e => e.PropertyId == propertyId);
        if (EstateText.Clean(f.ProjectId) is { } projectId)
            query = query.Where(e => e.ProjectId == projectId);
        if (EstateText.Clean(f.UnitTypeId) is { } unitTypeId)
            query = query.Where(e => e.UnitTypeId == unitTypeId);
        if (EstateText.Clean(f.LeadId) is { } leadId)
            query = query.Where(e => e.LeadId == leadId);
        if (EstateText.Clean(f.ContactId) is { } contactId)
            query = query.Where(e => e.ContactId == contactId);
        if (window && f.From is { } from)
        {
            var at = from.UtcDateTime;
            query = query.Where(e => e.OccurredAt >= at);
        }
        if (window && f.To is { } to)
        {
            var at = to.UtcDateTime;
            query = query.Where(e => e.OccurredAt < at);
        }
        foreach (var word in (f.Text ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6))
        {
            var digits = new string(word.Where(char.IsDigit).ToArray());
            var term = digits.Length >= 4 && digits.Length == word.TrimStart('+').Length ? digits : word;
            query = query.Where(e => e.SearchText.Contains(term));
        }
        return query;
    }
}

public static class EnquiryMapping
{
    private static DateTimeOffset Utc(DateTime at) => new(DateTime.SpecifyKind(at, DateTimeKind.Utc));

    public static EnquiryDto ToDto(EnquiryDocument e) => new(
        e.Id, e.Code, Utc(e.OccurredAt), e.Source, e.Form, e.LeadId, e.ContactId, e.IsNewLead, e.Name, e.Phone, e.Email,
        e.PropertyId, e.PropertyCode, e.PropertyTitle, e.ProjectId, e.ProjectName, e.UnitTypeId, e.UnitTypeName, e.Message,
        [.. e.Extra.Select(x => new EnquiryFieldDto(x.Key, x.Value))], e.AgentId, e.AgentName, e.Status, e.CloseReason, e.Wanted, e.CloseNote,
        e.RequirementId, e.VisitId, e.Ref, e.SourceUrl, e.ContactedAt is { } c ? Utc(c) : null, e.ClosedAt is { } cl ? Utc(cl) : null,
        Utc(e.CreatedAt), Utc(e.UpdatedAt));
}

/// <summary>This service's capture refs: <c>estate:{ObjectId}</c> — unique, and never a CMS id.</summary>
public static class EnquiryRefs
{
    public static string New() => "estate:" + ObjectId.GenerateNewId();
}

/// <summary>An Indian mobile number: 10 digits starting 6–9, with or without +91 / 91 / 0 in front. Returned as <c>+91…</c> (zeronova-lead's reading).</summary>
public static class IndianMobile
{
    public static string? Normalize(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
            digits = digits[2..];
        else if (digits.Length == 11 && digits[0] == '0')
            digits = digits[1..];
        return digits.Length == 10 && digits[0] is >= '6' and <= '9' ? "+91" + digits : null;
    }
}

/// <summary>
/// Enquiry counts for properties, projects and unit types (D30): one grouped query per kind and request, over the whole
/// company (every member sees the whole stock, D15), spam left out. A project counts its unit types' and units' enquiries
/// too; a unit type its units'.
/// </summary>
public sealed class EnquiryCounts(IEnquiryRepository enquiries, TimeProvider clock)
{
    private sealed class Row
    {
        public string? Key { get; init; }
        public EnquirySource Source { get; init; }
        public DateTime At { get; init; }
    }

    public Task<IReadOnlyDictionary<string, EnquiryCountsDto>> ForPropertiesAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return CountAsync(list, q => q.Where(e => list.Contains(e.PropertyId!)).Select(e => new Row { Key = e.PropertyId, Source = e.Source, At = e.OccurredAt }), ct);
    }

    public Task<IReadOnlyDictionary<string, EnquiryCountsDto>> ForProjectsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return CountAsync(list, q => q.Where(e => list.Contains(e.ProjectId!)).Select(e => new Row { Key = e.ProjectId, Source = e.Source, At = e.OccurredAt }), ct);
    }

    public Task<IReadOnlyDictionary<string, EnquiryCountsDto>> ForUnitTypesAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return CountAsync(list, q => q.Where(e => list.Contains(e.UnitTypeId!)).Select(e => new Row { Key = e.UnitTypeId, Source = e.Source, At = e.OccurredAt }), ct);
    }

    public static EnquiryCountsDto Of(IReadOnlyDictionary<string, EnquiryCountsDto> counts, string id) => counts.GetValueOrDefault(id) ?? EnquiryCountsDto.None;

    private async Task<IReadOnlyDictionary<string, EnquiryCountsDto>> CountAsync(IReadOnlyCollection<string> ids,
        Func<IQueryable<EnquiryDocument>, IQueryable<Row>> select, CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<string, EnquiryCountsDto>();
        var now = clock.GetUtcNow().UtcDateTime;
        var cut7 = now.AddDays(-7);
        var cut30 = now.AddDays(-30);
        var groups = await select(enquiries.GetDocuments(e => e.CloseReason != EnquiryCloseReason.Spam))
            .GroupBy(r => new { r.Key, r.Source })
            .Select(g => new { g.Key.Key, g.Key.Source, Total = g.Count(), Last7 = g.Sum(r => r.At >= cut7 ? 1 : 0), Last30 = g.Sum(r => r.At >= cut30 ? 1 : 0) })
            .ToListAsync(ct);
        return groups.Where(g => g.Key is not null).GroupBy(g => g.Key!).ToDictionary(g => g.Key, g => new EnquiryCountsDto(
            g.Sum(x => x.Total), g.Sum(x => x.Last7), g.Sum(x => x.Last30),
            [.. g.OrderBy(x => x.Source).Select(x => new EnquirySourceCountDto(x.Source, x.Total))]));
    }
}
