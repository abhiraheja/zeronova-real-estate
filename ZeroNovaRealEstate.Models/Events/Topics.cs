namespace ZeroNovaRealEstate.Models.Events;

/// <summary>Pub/Sub topics this service publishes to or consumes. Another service's topic keeps its owner's names.</summary>
public static class Topics
{
    /// <summary>zeronova-utility's dispatch: a bell (and push) for a member — here, new matches for an agent.</summary>
    public static class CommunicationEvents
    {
        public const string Topic = "communication-events";
        public const string DispatchEvent = "dispatch-event";
    }

    /// <summary>zeronova-lead's: a person to find or create in the contact book — here owners, buyers and tenants.</summary>
    public static class ContactRequests
    {
        public const string Topic = "contact-requests";
        public const string UpsertRequested = "contact-upsert-requested";
    }
}

/// <summary>
/// A person for zeronova-lead's contact book, found by <paramref name="Phone"/> (digits) or <paramref name="Email"/>, or
/// created with <paramref name="Tags"/>. The same shape as zeronova-lead's <c>ContactUpsertRequestedNotification</c>.
/// </summary>
/// <param name="ExternalRef">This service's id of the person (<c>party:&lt;id&gt;</c>), for the logs.</param>
/// <param name="WonReason">Set when a deal completes: the buyer's open enquiry (or <paramref name="LeadId"/>) closes as won.</param>
public sealed record ContactUpsertRequestedEvent(
    string CompanyId, string? Name, string? Phone, string? Email, IReadOnlyList<string> Tags, string ExternalRef,
    string? WonReason = null, string? LeadId = null);

/// <summary>zeronova-utility's <c>DispatchEventMessage</c> (a bell here: no realtime target). Field names are the contract.</summary>
public sealed record DispatchEventMessage(
    string EventId, string Type, string CompanyId, string Target, object? Data, DispatchDelivery Delivery, DispatchMeta Meta,
    DispatchNotification Notification);

public sealed record DispatchDelivery(bool Realtime, bool Persist, string Push);

public sealed record DispatchMeta(string Priority, string ProducedBy, string? CorrelationId, DateTimeOffset ProducedAt);

public sealed record DispatchNotification(
    string CompanyId, IReadOnlyList<string> Recipients, string Title, string? Body, string? Url, string? Icon);
