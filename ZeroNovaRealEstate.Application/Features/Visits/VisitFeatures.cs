using System.Globalization;
using System.Security.Cryptography;
using FluentValidation;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Configuration;
using ZeroNovaRealEstate.Models.Events;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Visits;

namespace ZeroNovaRealEstate.Application.Features.Visits;

// ── Site visits ───────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Schedules a site visit (M2): the property — or a project, its unit optional (design 7.3) — the customer and their
/// requirement, when, where to meet. The answer carries ready WhatsApp messages for the customer and the owner (sent
/// from the dealer's own WhatsApp).
/// </summary>
public sealed record ScheduleVisitCommand(VisitInput Input) : IRequest<VisitDto>;

public sealed class ScheduleVisitValidator : AbstractValidator<ScheduleVisitCommand>
{
    public ScheduleVisitValidator()
    {
        RuleFor(x => x.Input).Must(i => !string.IsNullOrWhiteSpace(i.PropertyId) || !string.IsNullOrWhiteSpace(i.ProjectId))
            .WithMessage("Pick the property or the project.");
        RuleFor(x => x.Input.LeadId).NotEmpty().WithMessage("Pick the customer (lead) first.");
        RuleFor(x => x.Input.Notes).MaximumLength(1000);
    }
}

public sealed class ScheduleVisitHandler(IVisitRepository visits, IPropertyRepository properties, IProjectRepository projects, IRequirementRepository requirements,
    VisitMapper mapper, EstateScope scope, TimeProvider clock, IEnquiryRepository enquiries, Customers.CustomerBook customers)
    : IRequestHandler<ScheduleVisitCommand, VisitDto>
{
    public async Task<VisitDto> Handle(ScheduleVisitCommand request, CancellationToken ct)
    {
        var input = request.Input;
        PropertyDocument? property = null;
        if (EstateText.Clean(input.PropertyId) is { } propertyId)
        {
            property = await properties.GetDocument(propertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
            if (property.Status is PropertyStatus.Sold or PropertyStatus.Rented or PropertyStatus.Withdrawn)
                throw new ValidatorException($"{property.Code} is {property.Status.ToString().ToLowerInvariant()}.");
        }
        ProjectDocument? project = null;
        if (property is not null && EstateText.Clean(input.ProjectId) is { } asked && asked != property.ProjectId)
            throw new ValidatorException($"{property.Code} is not a unit of that project.");
        if ((property?.ProjectId ?? EstateText.Clean(input.ProjectId)) is { } projectId)
        {
            project = await projects.GetDocument(projectId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Project not found.");
            if (property is not null && property.ProjectId != project.Id)
                throw new ValidatorException($"{property.Code} is not a unit of {project.Name}.");
        }
        if (input.At < clock.GetUtcNow().AddMinutes(-30))
            throw new ValidatorException("A visit is scheduled for a time to come.");
        RequirementDocument? requirement = null;
        if (EstateText.Clean(input.RequirementId) is { } requirementId)
        {
            requirement = await requirements.GetDocument(requirementId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Requirement not found.");
            scope.RequireWork(requirement);
        }
        var agent = EstateText.Clean(input.AgentId) ?? scope.UserId;
        if (agent != scope.UserId && !scope.SeesAllRequirements)
            throw new ValidatorException("Only a manager can give a visit to another agent.");
        // Booked from an enquiry (D31): the caller's to work, and the same customer.
        var enquiry = await Enquiries.EnquiryLinks.ForLinkAsync(enquiries, scope, input.EnquiryId, input.LeadId.Trim(), ct);
        var lead = input.LeadId.Trim();
        var contact = EstateText.Clean(input.ContactId) ?? (requirement?.LeadId == lead ? requirement.ContactId : null) ?? enquiry?.ContactId
            ?? await customers.ContactOfLeadAsync(lead, ct);

        var visit = new VisitDocument
        {
            Id = visits.GetNewId, PropertyId = property?.Id, ProjectId = project?.Id, RequirementId = EstateText.Clean(input.RequirementId), LeadId = lead, ContactId = contact,
            LeadName = EstateText.Clean(input.LeadName), LeadPhone = EstateText.Phone(input.LeadPhone), AgentId = agent, At = input.At.UtcDateTime,
            MeetingPoint = EstateText.Clean(input.MeetingPoint), Notes = EstateText.Clean(input.Notes),
        };
        await visits.AddAsync(visit, ct);
        if (enquiry is not null)
        {
            Enquiries.EnquiryLinks.ToVisit(enquiry, visit.Id, clock.GetUtcNow().UtcDateTime);
            await enquiries.UpdateAsync(enquiry, ct);
        }
        await customers.TouchAsync(visit.ContactId, [visit.LeadId], ct);
        return await mapper.OneAsync(visit, ct);
    }
}

/// <summary>A new time for a scheduled visit (the reminder goes again).</summary>
public sealed record RescheduleVisitCommand(string VisitId, DateTimeOffset At) : IRequest<VisitDto>;

public sealed class RescheduleVisitHandler(IVisitRepository visits, VisitMapper mapper, EstateScope scope, Customers.CustomerBook customers)
    : IRequestHandler<RescheduleVisitCommand, VisitDto>
{
    public async Task<VisitDto> Handle(RescheduleVisitCommand request, CancellationToken ct)
    {
        var visit = await VisitLookup.RequireAsync(visits, scope, request.VisitId, ct);
        if (visit.Status != VisitStatus.Scheduled)
            throw new ValidatorException("Only a scheduled visit can move.");
        visit.At = request.At.UtcDateTime;
        visit.Reminded = false;
        await visits.UpdateAsync(visit, ct);
        await customers.TouchAsync(visit.ContactId, [visit.LeadId], ct);
        return await mapper.OneAsync(visit, ct);
    }
}

/// <summary>
/// How the visit went: done (with the customer's outcome and words), no-show or cancelled. A done visit moves the
/// customer's match: interested or negotiating → interested; not interested → rejected with the feedback. A visit to a
/// unit moves its unit type's match; a visit to a project without a unit moves the customer's matches to that project.
/// </summary>
public sealed record RecordVisitResultCommand(string VisitId, VisitResultInput Input) : IRequest<VisitDto>;

public sealed class RecordVisitResultHandler(IVisitRepository visits, IMatchRepository matches, IPropertyRepository properties, VisitMapper mapper, EstateScope scope,
    TimeProvider clock, Customers.CustomerBook customers)
    : IRequestHandler<RecordVisitResultCommand, VisitDto>
{
    public async Task<VisitDto> Handle(RecordVisitResultCommand request, CancellationToken ct)
    {
        var visit = await VisitLookup.RequireAsync(visits, scope, request.VisitId, ct);
        var input = request.Input;
        if (input.Status == VisitStatus.Scheduled)
            throw new ValidatorException("Say how the visit went: done, no-show or cancelled.");
        visit.Status = input.Status;
        visit.Outcome = input.Status == VisitStatus.Done ? input.Outcome : null;
        visit.Feedback = EstateText.Clean(input.Feedback);
        await visits.UpdateAsync(visit, ct);

        if (visit.Status == VisitStatus.Done && visit.RequirementId is { } requirementId && visit.Outcome is { } outcome)
        {
            var moved = new List<MatchDocument>();
            if (visit.PropertyId is { } visited
                && await matches.GetDocuments(m => m.PropertyId == visited && m.RequirementId == requirementId).FirstOrDefaultAsync(ct) is { } exact)
                moved.Add(exact);
            else if (visit.ProjectId is { } projectId)
            {
                var unitTypeId = visit.PropertyId is { } unitId
                    ? await properties.GetDocuments(p => p.Id == unitId, true).Select(p => p.UnitTypeId).FirstOrDefaultAsync(ct)
                    : null;
                var query = matches.GetDocuments(m => m.RequirementId == requirementId && m.ProjectId == projectId);
                if (unitTypeId is not null)
                    query = query.Where(m => m.UnitTypeId == unitTypeId);
                moved.AddRange(await query.ToListAsync(ct));
            }
            foreach (var match in moved)
            {
                match.Status = outcome == VisitOutcome.NotInterested ? MatchStatus.Rejected : MatchStatus.Interested;
                match.RejectReason = match.Status == MatchStatus.Rejected ? visit.Feedback ?? "Not interested after the visit" : null;
                match.UpdatedAt = clock.GetUtcNow().UtcDateTime;
                await matches.UpdateAsync(match, ct);
            }
        }
        await customers.TouchAsync(visit.ContactId, [visit.LeadId], ct);
        return await mapper.OneAsync(visit, ct);
    }
}

/// <summary>Visits the caller may see — their own, or everyone's with PRQV — in a time window, of a property, a project or a requirement.</summary>
public sealed record GetVisitsQuery(DateTimeOffset? From, DateTimeOffset? To, string? PropertyId, string? RequirementId, VisitStatus? Status, string? ProjectId = null)
    : IRequest<IReadOnlyList<VisitDto>>;

public sealed class GetVisitsHandler(IVisitRepository visits, VisitMapper mapper, EstateScope scope) : IRequestHandler<GetVisitsQuery, IReadOnlyList<VisitDto>>
{
    public async Task<IReadOnlyList<VisitDto>> Handle(GetVisitsQuery request, CancellationToken ct)
    {
        var query = visits.GetDocuments();
        if (!scope.SeesAllRequirements)
        {
            var me = scope.UserId;
            query = query.Where(v => v.AgentId == me);
        }
        if (request.From is { } from)
        {
            var f = from.UtcDateTime;
            query = query.Where(v => v.At >= f);
        }
        if (request.To is { } to)
        {
            var t = to.UtcDateTime;
            query = query.Where(v => v.At < t);
        }
        if (EstateText.Clean(request.PropertyId) is { } propertyId)
            query = query.Where(v => v.PropertyId == propertyId);
        if (EstateText.Clean(request.RequirementId) is { } requirementId)
            query = query.Where(v => v.RequirementId == requirementId);
        if (EstateText.Clean(request.ProjectId) is { } projectId)
            query = query.Where(v => v.ProjectId == projectId);
        if (request.Status is { } status)
            query = query.Where(v => v.Status == status);
        return await mapper.ManyAsync(await query.OrderBy(v => v.At).Take(300).ToListAsync(ct), ct);
    }
}

internal static class VisitLookup
{
    public static async Task<VisitDocument> RequireAsync(IVisitRepository visits, EstateScope scope, string id, CancellationToken ct)
    {
        var visit = await visits.GetDocument(id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Visit not found.");
        if (!scope.SeesAllRequirements && visit.AgentId != scope.UserId)
            throw new ValidatorException("This visit is another agent's.");
        return visit;
    }
}

/// <summary>Visits as members see them, with the WhatsApp messages for the customer and the owner (owner's only with POWV).</summary>
public sealed class VisitMapper(IPropertyRepository properties, IProjectRepository projects, IPartyRepository parties, IEstateSettingsRepository settings,
    EstateScope scope)
{
    public async Task<VisitDto> OneAsync(VisitDocument visit, CancellationToken ct) => (await ManyAsync([visit], ct))[0];

    public async Task<IReadOnlyList<VisitDto>> ManyAsync(IReadOnlyList<VisitDocument> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];
        var s = await EstateSettings.LoadAsync(settings, ct);
        var ids = rows.Select(v => v.PropertyId).OfType<string>().Distinct().ToList();
        var props = (await properties.GetDocuments(p => ids.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        var projectIds = rows.Select(v => v.ProjectId).OfType<string>().Distinct().ToList();
        var projs = projectIds.Count == 0
            ? new Dictionary<string, ProjectDocument>()
            : (await projects.GetDocuments(p => projectIds.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        var ownerIds = props.Values.Select(p => p.Owners.FirstOrDefault()?.PartyId).OfType<string>().Distinct().ToList();
        var owners = (await parties.GetDocuments(p => ownerIds.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        return [.. rows.Where(v => v.PropertyId is { } id ? props.ContainsKey(id) : v.ProjectId is { } pj && projs.ContainsKey(pj)).Select(v =>
        {
            var p = v.PropertyId is { } id ? props[id] : null;
            var project = v.ProjectId is { } pj ? projs.GetValueOrDefault(pj) : null;
            var code = p?.Code ?? project!.Code;
            var title = p?.Title ?? project!.Name;
            var owner = p?.Owners.FirstOrDefault()?.PartyId is { } oid ? owners.GetValueOrDefault(oid) : null;
            var when = Drafts.When(v.At);
            var client = $"Namaste {v.LeadName ?? "ji"}, your visit to see {(p is not null ? "the " + p.Title : project!.Name)} is fixed for {when}."
                + (v.MeetingPoint is { } mp ? $" We meet at {mp}." : string.Empty)
                + (s.DealerName is { } dn ? $" — {dn}" : string.Empty);
            var ownerText = scope.SeesOwners && owner is not null
                ? $"Namaste {owner.Name}, a customer will visit your {title} on {when}. Please keep it open and ready."
                    + (s.DealerName is { } dealer ? $" — {dealer}" : string.Empty)
                : null;
            return new VisitDto(v.Id, p?.Id, code, title, v.RequirementId, v.LeadId, v.LeadName, v.LeadPhone, v.AgentId,
                new DateTimeOffset(DateTime.SpecifyKind(v.At, DateTimeKind.Utc)), v.MeetingPoint, v.Status, v.Outcome, v.Feedback, v.Notes, p?.KeysWith,
                p?.VisitTimings, scope.SeesOwners ? owner?.Phone : null, client, ownerText, project?.Id, project?.Name);
        })];
    }
}

/// <summary>A bell to the agent for each visit starting within the hour, once (across companies; no caller).</summary>
public sealed class VisitReminders(IVisitRepository visits, IPropertyRepository properties, IProjectRepository projects, IAgentNotificationProducer bell,
    TimeProvider clock, ILogger<VisitReminders> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await visits.DueForReminderAsync(now.AddHours(1).UtcDateTime, 200, ct);
        foreach (var visit in due)
        {
            visit.Reminded = true;
            await visits.UpdateAsync(visit, ct, visit.CompanyId);
            if (visit.At < now.AddHours(-2).UtcDateTime)
                continue;   // long past: marked, not rung
            var what = visit.PropertyId is { } propertyId
                ? (await properties.GetDocument(propertyId, true, visit.CompanyId).FirstOrDefaultAsync(ct))?.Title
                : visit.ProjectId is { } projectId ? (await projects.GetDocument(projectId, true, visit.CompanyId).FirstOrDefaultAsync(ct))?.Name : null;
            try
            {
                await bell.PublishAsync(new DispatchEventMessage(Guid.NewGuid().ToString(), "ESTATE_VISIT_SOON", visit.CompanyId, string.Empty,
                    new { visitId = visit.Id }, new DispatchDelivery(false, true, "IfOffline"),
                    new DispatchMeta("high", "zeronova-real-estate", visit.Id, now),
                    new DispatchNotification(visit.CompanyId, [visit.AgentId], $"Site visit at {Drafts.Time(visit.At)}: {visit.LeadName ?? "customer"}",
                        what, "/real-estate/visits", "calendar-event")), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Visit reminder {VisitId} not rung.", visit.Id);
            }
        }
        return due.Count;
    }
}

// ── Sharing ───────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A link to send a property (M2): a page with photos and details, never the owner or the unit number, that counts
/// its opens. For a customer's requirement it marks their match shown. The answer carries the WhatsApp message.
/// </summary>
public sealed record CreateShareCommand(string PropertyId, string? RequirementId) : IRequest<ShareDto>;

public sealed class CreateShareHandler(IShareRepository shares, IPropertyRepository properties, IRequirementRepository requirements, IMatchRepository matches,
    IUnitTypeRepository unitTypes, ShareOptions options, EstateScope scope, TimeProvider clock)
    : IRequestHandler<CreateShareCommand, ShareDto>
{
    public async Task<ShareDto> Handle(CreateShareCommand request, CancellationToken ct)
    {
        var property = await properties.GetDocument(request.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        RequirementDocument? requirement = null;
        if (EstateText.Clean(request.RequirementId) is { } requirementId)
        {
            requirement = await requirements.GetDocument(requirementId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Requirement not found.");
            scope.RequireWork(requirement);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var share = new ShareDocument
        {
            Id = shares.GetNewId, Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_').TrimEnd('='),
            PropertyId = property.Id, RequirementId = requirement?.Id, CreatedAt = now,
        };
        await shares.AddAsync(share, ct);

        if (requirement is not null)
        {
            var match = await matches.GetDocuments(m => m.PropertyId == property.Id && m.RequirementId == requirement.Id).FirstOrDefaultAsync(ct);
            if (match is { Status: MatchStatus.New })
            {
                match.Status = MatchStatus.Shown;
                match.UpdatedAt = now;
                await matches.UpdateAsync(match, ct);
            }
        }
        return ShareMapping.ToDto(share, property, options, requirement?.LeadName, await ShareMapping.PriceOnRequestAsync(property, unitTypes, ct));
    }
}

public sealed record GetSharesQuery(string PropertyId) : IRequest<IReadOnlyList<ShareDto>>;

public sealed class GetSharesHandler(IShareRepository shares, IPropertyRepository properties, IRequirementRepository requirements, IUnitTypeRepository unitTypes,
    ShareOptions options)
    : IRequestHandler<GetSharesQuery, IReadOnlyList<ShareDto>>
{
    public async Task<IReadOnlyList<ShareDto>> Handle(GetSharesQuery request, CancellationToken ct)
    {
        var property = await properties.GetDocument(request.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        var rows = await shares.GetDocuments(s => s.PropertyId == property.Id).OrderByDescending(s => s.CreatedAt).Take(100).ToListAsync(ct);
        var reqIds = rows.Select(r => r.RequirementId).OfType<string>().Distinct().ToList();
        var names = (await requirements.GetDocuments(r => reqIds.Contains(r.Id), true).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.LeadName);
        var onRequest = await ShareMapping.PriceOnRequestAsync(property, unitTypes, ct);
        return [.. rows.Select(r => ShareMapping.ToDto(r, property, options, r.RequirementId is { } id ? names.GetValueOrDefault(id) : null, onRequest))];
    }
}

/// <summary>The shared page, for anyone with the link: the property as the customer may see it. Each open is counted.</summary>
public sealed record GetSharedPropertyQuery(string Token) : IRequest<SharedPropertyDto>;

public sealed class GetSharedPropertyHandler(IShareRepository shares, IPropertyRepository properties, IUnitTypeRepository unitTypes, IEstateSettingsRepository settings,
    TimeProvider clock)
    : IRequestHandler<GetSharedPropertyQuery, SharedPropertyDto>
{
    public async Task<SharedPropertyDto> Handle(GetSharedPropertyQuery request, CancellationToken ct)
    {
        var share = await shares.FindByTokenAnyTenantAsync(request.Token.Trim(), ct) ?? throw new ValidatorException("This link is not valid.");
        if (share.PropertyId is not { } propertyId)
            throw new ValidatorException("This link is not valid.");
        var p = await properties.GetDocument(propertyId, true, share.CompanyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("This link is not valid.");
        var s = await EstateSettings.LoadAsync(settings, ct, share.CompanyId);
        var onRequest = await ShareMapping.PriceOnRequestAsync(p, unitTypes, ct, share.CompanyId);
        await shares.RecordOpenAsync(share.Id, share.CompanyId, clock.GetUtcNow().UtcDateTime, ct);
        return new SharedPropertyDto(p.Title, p.Purpose, p.Type, p.City, p.Locality, p.Society, AreaMath.Dto(p.PlotArea), AreaMath.Dto(p.BuiltUpArea),
            AreaMath.Dto(p.CarpetArea), AreaMath.Dto(p.SuperArea), onRequest ? null : p.Price, p.Negotiable, p.Rent, p.Deposit, p.Bhk, p.Bathrooms, p.Floor, p.TotalFloors,
            p.Furnishing, p.Facing, p.Corner, p.ParkFacing, p.Amenities, [.. p.Photos.Select(x => new PhotoDto(x.Key, x.Url))], p.VideoUrl,
            s.DealerName, s.DealerPhone, s.ReraNumber, !p.IsDeleted && p.Status is PropertyStatus.Available or PropertyStatus.OnHold);
    }
}

/// <summary>"Still available?" to the property's first owner on WhatsApp (D6) — a ready message for the dealer's WhatsApp.</summary>
public sealed record GetOwnerCheckDraftQuery(string PropertyId) : IRequest<WhatsAppDraftDto>;

public sealed class GetOwnerCheckDraftHandler(IPropertyRepository properties, IPartyRepository parties, IEstateSettingsRepository settings)
    : IRequestHandler<GetOwnerCheckDraftQuery, WhatsAppDraftDto>
{
    public async Task<WhatsAppDraftDto> Handle(GetOwnerCheckDraftQuery request, CancellationToken ct)
    {
        var p = await properties.GetDocument(request.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        var s = await EstateSettings.LoadAsync(settings, ct);
        var owner = p.Owners.FirstOrDefault()?.PartyId is { } id ? await parties.GetDocument(id).FirstOrDefaultAsync(ct) : null;
        var price = p.PriceValue is { } v ? $" at {EstateText.Rupees(v)}{(p.Purpose == PropertyPurpose.Rent ? " a month" : string.Empty)}" : string.Empty;
        var text = $"Namaste {owner?.Name ?? "ji"}, is your {p.Title}{price} still available? Please reply: 1 = Yes, still available · 2 = Sold / rented · 3 = Price changed."
            + (s.DealerName is { } dn ? $" — {dn}" : string.Empty);
        return new WhatsAppDraftDto(owner?.WhatsApp ?? owner?.Phone ?? p.SourceDealerPhone, text, Drafts.WaLink(owner?.WhatsApp ?? owner?.Phone ?? p.SourceDealerPhone, text));
    }
}

internal static class ShareMapping
{
    public static ShareDto ToDto(ShareDocument s, PropertyDocument p, ShareOptions options, string? leadName, bool priceOnRequest = false)
    {
        var url = $"{options.BaseUrl.TrimEnd('/')}/p/{s.Token}";
        var price = !priceOnRequest && p.PriceValue is { } v ? $"\n{EstateText.Rupees(v)}{(p.Purpose == PropertyPurpose.Rent ? " / month" : string.Empty)}" : string.Empty;
        var message = $"Namaste {leadName ?? "ji"}, here is a property for you: {p.Title}{price}\nPhotos and details: {url}";
        return new ShareDto(s.Id, s.Token, url, p.Id, s.RequirementId, s.Opens,
            s.LastOpenedAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
            new DateTimeOffset(DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc)), message, s.Enquiries);
    }

    /// <summary>A project unit whose type is "price on request" (D22) shows no price to a customer.</summary>
    public static async Task<bool> PriceOnRequestAsync(PropertyDocument p, IUnitTypeRepository unitTypes, CancellationToken ct, string? companyId = null) =>
        p.UnitTypeId is { } typeId
        && await unitTypes.GetDocuments(t => t.Id == typeId, true, companyId).Select(t => t.PriceOnRequest).FirstOrDefaultAsync(ct);
}

/// <summary>Times in India and WhatsApp links as dealers send them.</summary>
public static class Drafts
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    /// <summary>"Tue 28 Oct, 5:30 PM" (India time).</summary>
    public static string When(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(India)
        .ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>"5:30 PM" (India time).</summary>
    public static string Time(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(India)
        .ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>wa.me with the text, or null without a number.</summary>
    public static string? WaLink(string? phone, string text) =>
        phone is { Length: > 0 } ? $"https://wa.me/{phone}?text={Uri.EscapeDataString(text)}" : null;
}
