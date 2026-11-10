using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNova_Packages.Messaging.Abstractions;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Projects;
using ZeroNovaRealEstate.Application.Features.Visits;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Events;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Application.Features.Matching;

/// <summary>
/// The one-click "now available" message (D33) for a match: a fresh share link for the match's requirement (the property's
/// page, or the project's for a unit-type match), then — when the dealer set an approved WhatsApp template
/// (<c>matchTemplateName</c>) and the customer has a phone — the template to zeronova-social's
/// <c>whatsapp-first-touch-requested</c> (customer name, property, price, link), sent from the company's default line;
/// otherwise the ready wa.me message (M2's drafts) for the app to open. Either way the match is now shown and never offered
/// again. zeronova-social checks the line and the template at send time; a refusal comes back on <c>whatsapp-failed</c> and
/// is kept on the match (<see cref="MatchDto.MessageFailed"/>), with a bell to the agent.
/// </summary>
public sealed record SendMatchMessageCommand(string MatchId) : IRequest<MatchMessageDto>;

public sealed class SendMatchMessageHandler(
    IMatchRepository matches, IRequirementRepository requirements, IPropertyRepository properties, IUnitTypeRepository unitTypes,
    IEstateSettingsRepository settings, IWhatsAppTemplateProducer whatsapp, IMediator mediator, IRequestContext context, EstateScope scope, TimeProvider clock)
    : IRequestHandler<SendMatchMessageCommand, MatchMessageDto>
{
    public async Task<MatchMessageDto> Handle(SendMatchMessageCommand request, CancellationToken ct)
    {
        var id = request.MatchId?.Trim() ?? string.Empty;
        var match = ObjectId.TryParse(id, out _) ? await matches.GetDocument(id).FirstOrDefaultAsync(ct) : null;
        if (match is null)
            throw new ValidatorException("Match not found.");
        if (!scope.SeesAllRequirements && match.AgentId != scope.UserId)
            throw new ValidatorException("This customer is another agent's.");
        if (match.Status == MatchStatus.Rejected)
            throw new ValidatorException("The customer turned this one down; it is not offered again.");
        var requirement = await requirements.GetDocument(match.RequirementId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Requirement not found.");
        scope.RequireWork(requirement);
        var property = await properties.GetDocument(match.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        if (property.Status != PropertyStatus.Available)
            throw new ValidatorException($"{property.Code} is not available any more.");

        // A fresh link, made for the requirement — which also marks the match shown (M2, design 7.3).
        string shareId, shareUrl, title, price;
        var onRequest = await ShareMapping.PriceOnRequestAsync(property, unitTypes, ct);
        if (match.ProjectId is { } projectId)
        {
            var share = await mediator.Send(new CreateProjectShareCommand(projectId, requirement.Id), ct);
            (shareId, shareUrl) = (share.Id, share.Url);
            title = $"{match.UnitTypeName ?? property.UnitTypeName ?? "A unit"} in {match.ProjectName ?? "the project"}";
            price = onRequest || property.PriceValue is not { } from ? "Price on request" : $"From {EstateText.Rupees(from)}";
        }
        else
        {
            var share = await mediator.Send(new CreateShareCommand(property.Id, requirement.Id), ct);
            (shareId, shareUrl) = (share.Id, share.Url);
            title = property.Title;
            price = onRequest || property.PriceValue is not { } value
                ? "Price on request"
                : EstateText.Rupees(value) + (property.Purpose == PropertyPurpose.Rent ? " a month" : string.Empty);
        }

        var s = await EstateSettings.LoadAsync(settings, ct);
        var name = requirement.LeadName ?? "ji";
        var text = $"Namaste {name}, you had asked us for a property like this — it is available now: {title}\n{price}\nPhotos and details: {shareUrl}"
                   + (s.DealerName is { } dealer ? $"\n— {dealer}" : string.Empty);
        var phone = requirement.LeadPhone;
        var now = clock.GetUtcNow().UtcDateTime;

        // The share marked it shown; read it again (its version moved) and keep how it was sent.
        match = await matches.GetDocument(match.Id).FirstOrDefaultAsync(ct) ?? match;
        if (match.Status == MatchStatus.New)
            match.Status = MatchStatus.Shown;
        match.MessagedAt = now;
        match.MessageFailed = null;
        match.UpdatedAt = now;

        var viaTemplate = s.MatchTemplateName is { Length: > 0 } && phone is { Length: > 0 };
        if (viaTemplate)
        {
            var company = context.CompanyId ?? throw new ValidatorException("No active company. Select a company first.");
            match.MessageVia = MatchMessageVia.WhatsAppTemplate;
            match.MessageKey = MatchMessages.Key(company, match.Id, shareId);
            await whatsapp.PublishAsync(new WhatsAppFirstTouchRequestedEvent(company, match.MessageKey, phone!, s.MatchTemplateName!, s.MatchTemplateLanguage,
                [name, title, price, shareUrl], null, null, null, requirement.LeadName, scope.UserId, requirement.LeadId), ct);
        }
        else
        {
            match.MessageVia = MatchMessageVia.Draft;
            match.MessageKey = null;
        }
        await matches.UpdateAsync(match, ct);

        var dto = (await MatchMapping.ManyAsync([match], properties, requirements, ct))[0];
        return new MatchMessageDto(viaTemplate, match.MessageVia.Value, viaTemplate ? null : Drafts.WaLink(phone, text), shareUrl, text, phone, dto);
    }
}

public static class MatchMessages
{
    public const string KeyPrefix = "re:";

    /// <summary><c>re:{companyId}:m:{matchId}:{shareId}</c> — ObjectIds only (zeronova-office reads keys with a UUID part as its own).</summary>
    public static string Key(string companyId, string matchId, string shareId) => $"{KeyPrefix}{companyId}:m:{matchId}:{shareId}";
}

/// <summary>
/// zeronova-social's <c>whatsapp-failed</c> (<c>WhatsAppSendOutcomeEvent</c>); field names are social's. Every failed send
/// of every service comes here; only this service's keys (<c>re:…</c>) are read.
/// </summary>
public sealed class WhatsAppSendFailedNotification : ConsumerNotification
{
    public string CompanyId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public string? WhatsAppMessageId { get; set; }
    public string? Form { get; set; }
    public string? TemplateName { get; set; }
    public string? Error { get; set; }
    public DateTime OccurredAt { get; set; }

    /// <summary>bad_request, not_connected, account_error, template_not_approved, blocked, rate_limited, refused.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// A one-click message WhatsApp did not send (D33): the reason is kept on the match, and the agent is told to send it from
/// their phone (the match stays shown: the customer is not offered it again as new). Idempotent.
/// </summary>
public sealed class WhatsAppSendFailedHandler(IMatchRepository matches, IRequirementRepository requirements, IAgentNotificationProducer bell, TimeProvider clock,
    ILogger<WhatsAppSendFailedHandler> logger)
    : INotificationHandler<WhatsAppSendFailedNotification>
{
    public async Task Handle(WhatsAppSendFailedNotification n, CancellationToken ct)
    {
        if (!n.IdempotencyKey.StartsWith(MatchMessages.KeyPrefix, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(n.CompanyId))
            return;
        var company = n.CompanyId;
        var key = n.IdempotencyKey;
        var match = await matches.GetDocuments(m => m.MessageKey == key, false, company).FirstOrDefaultAsync(ct);
        if (match is null || match.MessageFailed is not null)
            return;
        match.MessageFailed = n.Reason ?? "failed";
        match.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await matches.UpdateAsync(match, ct, company);

        var requirement = await requirements.GetDocument(match.RequirementId, true, company).FirstOrDefaultAsync(ct);
        var why = n.Reason switch
        {
            "not_connected" => "no WhatsApp number is connected",
            "template_not_approved" => "the WhatsApp template is not approved",
            "blocked" => "the customer blocked the number",
            "rate_limited" => "too many WhatsApp messages today",
            _ => n.Error ?? "WhatsApp refused it",
        };
        try
        {
            await bell.PublishAsync(new DispatchEventMessage(
                Guid.NewGuid().ToString(), "ESTATE_MESSAGE_FAILED", company, string.Empty, new { url = $"/real-estate/requirements/{match.RequirementId}" },
                new DispatchDelivery(Realtime: false, Persist: true, Push: "IfOffline"),
                new DispatchMeta("normal", "zeronova-real-estate", match.Id, clock.GetUtcNow()),
                new DispatchNotification(company, [match.AgentId], $"WhatsApp to {requirement?.LeadName ?? "the customer"} was not sent",
                    $"{char.ToUpperInvariant(why[0])}{why[1..]}. Send it from your phone.", $"/real-estate/requirements/{match.RequirementId}", "building")), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed-message bell for match {MatchId} not sent.", match.Id);
        }
    }
}
