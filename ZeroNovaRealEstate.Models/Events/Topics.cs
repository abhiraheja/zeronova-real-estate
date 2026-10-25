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
}

/// <summary>zeronova-utility's <c>DispatchEventMessage</c> (a bell here: no realtime target). Field names are the contract.</summary>
public sealed record DispatchEventMessage(
    string EventId, string Type, string CompanyId, string Target, object? Data, DispatchDelivery Delivery, DispatchMeta Meta,
    DispatchNotification Notification);

public sealed record DispatchDelivery(bool Realtime, bool Persist, string Push);

public sealed record DispatchMeta(string Priority, string ProducedBy, string? CorrelationId, DateTimeOffset ProducedAt);

public sealed record DispatchNotification(
    string CompanyId, IReadOnlyList<string> Recipients, string Title, string? Body, string? Url, string? Icon);
