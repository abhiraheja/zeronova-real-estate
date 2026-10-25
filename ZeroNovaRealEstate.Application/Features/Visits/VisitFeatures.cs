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
/// Schedules a site visit (M2): the property, the customer and their requirement, when, where to meet. The answer
/// carries ready WhatsApp messages for the customer and the owner (sent from the dealer's own WhatsApp).
/// </summary>
public sealed record ScheduleVisitCommand(VisitInput Input) : IRequest<VisitDto>;

public sealed class ScheduleVisitValidator : AbstractValidator<ScheduleVisitCommand>
{
    public ScheduleVisitValidator()
    {
        RuleFor(x => x.Input.PropertyId).NotEmpty();
        RuleFor(x => x.Input.LeadId).NotEmpty().WithMessage("Pick the customer (lead) first.");
        RuleFor(x => x.Input.Notes).MaximumLength(1000);
    }
}

public sealed class ScheduleVisitHandler(IVisitRepository visits, IPropertyRepository properties, IRequirementRepository requirements, VisitMapper mapper,
    EstateScope scope, TimeProvider clock)
    : IRequestHandler<ScheduleVisitCommand, VisitDto>
{
    public async Task<VisitDto> Handle(ScheduleVisitCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var property = await properties.GetDocument(input.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        if (property.Status is PropertyStatus.Sold or PropertyStatus.Rented or PropertyStatus.Withdrawn)
            throw new ValidatorException($"{property.Code} is {property.Status.ToString().ToLowerInvariant()}.");
        if (input.At < clock.GetUtcNow().AddMinutes(-30))
            throw new ValidatorException("A visit is scheduled for a time to come.");
        if (EstateText.Clean(input.RequirementId) is { } requirementId)
            scope.RequireWork(await requirements.GetDocument(requirementId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Requirement not found."));
        var agent = EstateText.Clean(input.AgentId) ?? scope.UserId;
        if (agent != scope.UserId && !scope.SeesAllRequirements)
            throw new ValidatorException("Only a manager can give a visit to another agent.");

        var visit = new VisitDocument
        {
            Id = visits.GetNewId, PropertyId = property.Id, RequirementId = EstateText.Clean(input.RequirementId), LeadId = input.LeadId.Trim(),
            LeadName = EstateText.Clean(input.LeadName), LeadPhone = EstateText.Phone(input.LeadPhone), AgentId = agent, At = input.At.UtcDateTime,
            MeetingPoint = EstateText.Clean(input.MeetingPoint), Notes = EstateText.Clean(input.Notes),
        };
        await visits.AddAsync(visit, ct);
        return await mapper.OneAsync(visit, ct);
    }
}

/// <summary>A new time for a scheduled visit (the reminder goes again).</summary>
public sealed record RescheduleVisitCommand(string VisitId, DateTimeOffset At) : IRequest<VisitDto>;

public sealed class RescheduleVisitHandler(IVisitRepository visits, VisitMapper mapper, EstateScope scope) : IRequestHandler<RescheduleVisitCommand, VisitDto>
{
    public async Task<VisitDto> Handle(RescheduleVisitCommand request, CancellationToken ct)
    {
        var visit = await VisitLookup.RequireAsync(visits, scope, request.VisitId, ct);
        if (visit.Status != VisitStatus.Scheduled)
            throw new ValidatorException("Only a scheduled visit can move.");
        visit.At = request.At.UtcDateTime;
        visit.Reminded = false;
        await visits.UpdateAsync(visit, ct);
        return await mapper.OneAsync(visit, ct);
    }
}

/// <summary>
/// How the visit went: done (with the customer's outcome and words), no-show or cancelled. A done visit moves the
/// customer's match: interested or negotiating → interested; not interested → rejected with the feedback.
/// </summary>
public sealed record RecordVisitResultCommand(string VisitId, VisitResultInput Input) : IRequest<VisitDto>;

public sealed class RecordVisitResultHandler(IVisitRepository visits, IMatchRepository matches, VisitMapper mapper, EstateScope scope, TimeProvider clock)
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
            var match = await matches.GetDocuments(m => m.PropertyId == visit.PropertyId && m.RequirementId == requirementId).FirstOrDefaultAsync(ct);
            if (match is not null)
            {
                match.Status = outcome == VisitOutcome.NotInterested ? MatchStatus.Rejected : MatchStatus.Interested;
                match.RejectReason = match.Status == MatchStatus.Rejected ? visit.Feedback ?? "Not interested after the visit" : null;
                match.UpdatedAt = clock.GetUtcNow().UtcDateTime;
                await matches.UpdateAsync(match, ct);
            }
        }
        return await mapper.OneAsync(visit, ct);
    }
}

/// <summary>Visits the caller may see — their own, or everyone's with PRQV — in a time window, of a property or a requirement.</summary>
public sealed record GetVisitsQuery(DateTimeOffset? From, DateTimeOffset? To, string? PropertyId, string? RequirementId, VisitStatus? Status)
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
public sealed class VisitMapper(IPropertyRepository properties, IPartyRepository parties, IEstateSettingsRepository settings, EstateScope scope)
{
    public async Task<VisitDto> OneAsync(VisitDocument visit, CancellationToken ct) => (await ManyAsync([visit], ct))[0];

    public async Task<IReadOnlyList<VisitDto>> ManyAsync(IReadOnlyList<VisitDocument> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];
        var s = await EstateSettings.LoadAsync(settings, ct);
        var ids = rows.Select(v => v.PropertyId).Distinct().ToList();
        var props = (await properties.GetDocuments(p => ids.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        var ownerIds = props.Values.Select(p => p.Owners.FirstOrDefault()?.PartyId).OfType<string>().Distinct().ToList();
        var owners = (await parties.GetDocuments(p => ownerIds.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        return [.. rows.Where(v => props.ContainsKey(v.PropertyId)).Select(v =>
        {
            var p = props[v.PropertyId];
            var owner = p.Owners.FirstOrDefault()?.PartyId is { } oid ? owners.GetValueOrDefault(oid) : null;
            var when = Drafts.When(v.At);
            var client = $"Namaste {v.LeadName ?? "ji"}, your visit to see the {p.Title} is fixed for {when}."
                + (v.MeetingPoint is { } mp ? $" We meet at {mp}." : string.Empty)
                + (s.DealerName is { } dn ? $" — {dn}" : string.Empty);
            var ownerText = scope.SeesOwners && owner is not null
                ? $"Namaste {owner.Name}, a customer will visit your {p.Title} on {when}. Please keep it open and ready."
                    + (s.DealerName is { } dealer ? $" — {dealer}" : string.Empty)
                : null;
            return new VisitDto(v.Id, p.Id, p.Code, p.Title, v.RequirementId, v.LeadId, v.LeadName, v.LeadPhone, v.AgentId,
                new DateTimeOffset(DateTime.SpecifyKind(v.At, DateTimeKind.Utc)), v.MeetingPoint, v.Status, v.Outcome, v.Feedback, v.Notes, p.KeysWith,
                p.VisitTimings, scope.SeesOwners ? owner?.Phone : null, client, ownerText);
        })];
    }
}

/// <summary>A bell to the agent for each visit starting within the hour, once (across companies; no caller).</summary>
public sealed class VisitReminders(IVisitRepository visits, IPropertyRepository properties, IAgentNotificationProducer bell, TimeProvider clock,
    ILogger<VisitReminders> logger)
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
            var property = await properties.GetDocument(visit.PropertyId, true, visit.CompanyId).FirstOrDefaultAsync(ct);
            try
            {
                await bell.PublishAsync(new DispatchEventMessage(Guid.NewGuid().ToString(), "ESTATE_VISIT_SOON", visit.CompanyId, string.Empty,
                    new { visitId = visit.Id }, new DispatchDelivery(false, true, "IfOffline"),
                    new DispatchMeta("high", "zeronova-real-estate", visit.Id, now),
                    new DispatchNotification(visit.CompanyId, [visit.AgentId], $"Site visit at {Drafts.Time(visit.At)}: {visit.LeadName ?? "customer"}",
                        property?.Title, "/real-estate/visits", "calendar-event")), ct);
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
    ShareOptions options, EstateScope scope, TimeProvider clock)
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
        return ShareMapping.ToDto(share, property, options, requirement?.LeadName);
    }
}

public sealed record GetSharesQuery(string PropertyId) : IRequest<IReadOnlyList<ShareDto>>;

public sealed class GetSharesHandler(IShareRepository shares, IPropertyRepository properties, IRequirementRepository requirements, ShareOptions options)
    : IRequestHandler<GetSharesQuery, IReadOnlyList<ShareDto>>
{
    public async Task<IReadOnlyList<ShareDto>> Handle(GetSharesQuery request, CancellationToken ct)
    {
        var property = await properties.GetDocument(request.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        var rows = await shares.GetDocuments(s => s.PropertyId == property.Id).OrderByDescending(s => s.CreatedAt).Take(100).ToListAsync(ct);
        var reqIds = rows.Select(r => r.RequirementId).OfType<string>().Distinct().ToList();
        var names = (await requirements.GetDocuments(r => reqIds.Contains(r.Id), true).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.LeadName);
        return [.. rows.Select(r => ShareMapping.ToDto(r, property, options, r.RequirementId is { } id ? names.GetValueOrDefault(id) : null))];
    }
}

/// <summary>The shared page, for anyone with the link: the property as the customer may see it. Each open is counted.</summary>
public sealed record GetSharedPropertyQuery(string Token) : IRequest<SharedPropertyDto>;

public sealed class GetSharedPropertyHandler(IShareRepository shares, IPropertyRepository properties, IEstateSettingsRepository settings, TimeProvider clock)
    : IRequestHandler<GetSharedPropertyQuery, SharedPropertyDto>
{
    public async Task<SharedPropertyDto> Handle(GetSharedPropertyQuery request, CancellationToken ct)
    {
        var share = await shares.FindByTokenAnyTenantAsync(request.Token.Trim(), ct) ?? throw new ValidatorException("This link is not valid.");
        var p = await properties.GetDocument(share.PropertyId, true, share.CompanyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("This link is not valid.");
        var s = await EstateSettings.LoadAsync(settings, ct, share.CompanyId);
        await shares.RecordOpenAsync(share.Id, share.CompanyId, clock.GetUtcNow().UtcDateTime, ct);
        return new SharedPropertyDto(p.Title, p.Purpose, p.Type, p.City, p.Locality, p.Society, AreaMath.Dto(p.PlotArea), AreaMath.Dto(p.BuiltUpArea),
            AreaMath.Dto(p.CarpetArea), AreaMath.Dto(p.SuperArea), p.Price, p.Negotiable, p.Rent, p.Deposit, p.Bhk, p.Bathrooms, p.Floor, p.TotalFloors,
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
    public static ShareDto ToDto(ShareDocument s, PropertyDocument p, ShareOptions options, string? leadName)
    {
        var url = $"{options.BaseUrl.TrimEnd('/')}/p/{s.Token}";
        var price = p.PriceValue is { } v ? $"\n{EstateText.Rupees(v)}{(p.Purpose == PropertyPurpose.Rent ? " / month" : string.Empty)}" : string.Empty;
        var message = $"Namaste {leadName ?? "ji"}, here is a property for you: {p.Title}{price}\nPhotos and details: {url}";
        return new ShareDto(s.Id, s.Token, url, p.Id, s.RequirementId, s.Opens,
            s.LastOpenedAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
            new DateTimeOffset(DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc)), message);
    }
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
