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

    /// <summary>zeronova-lead's: an enquiry to record on a lead (found or made) — here the share page's form and "Add enquiry" (D29).</summary>
    public static class LeadCaptured
    {
        public const string Topic = "lead-captured";
        public const string Enquiry = "lead-captured-enquiry";
    }

    /// <summary>zeronova-lead's lead events; this service reads <c>lead-enquiry-received</c> (D28).</summary>
    public static class LeadLifecycle
    {
        public const string Topic = "lead-lifecycle";
        public const string EnquiryReceived = "lead-enquiry-received";

        /// <summary>The subscription (prefixed with <c>Messaging:Source</c>: <c>zeronova-real-estate.lead-enquiry-sub</c>).</summary>
        public const string EnquirySubscription = "lead-enquiry-sub";
    }
}

/// <summary>
/// zeronova-lead's <c>LeadEnquiryCapturedNotification</c> (<c>lead-captured-enquiry</c>); field names are the contract.
/// <see cref="LeadSource"/> and <see cref="Channel"/> are zeronova-lead's enum names (<see cref="LeadSources"/>,
/// <see cref="ActivityChannels"/>). <see cref="ExternalRef"/> is the dedup key and comes back on
/// <c>lead-enquiry-received</c>. Only <see cref="CompanyId"/> and one of <see cref="LeadId"/> / <see cref="Phone"/> /
/// <see cref="Email"/> are needed.
/// </summary>
public sealed record LeadCapturedEnquiryEvent(
    string CompanyId, string LeadSource, string Channel, string? Name, string? Phone, string? Email, string? Message,
    IReadOnlyDictionary<string, string> Metadata, string? SourceUrl, string ExternalRef, DateTimeOffset CapturedAt, string? AssignTo, string? AssignToName,
    string? LeadId);

/// <summary>zeronova-lead's <c>LeadSource</c> names this service sends.</summary>
public static class LeadSources
{
    public const string Website = "Website";
    public const string ManualCall = "ManualCall";
    public const string Manual = "Manual";
    public const string WhatsApp = "WhatsApp";
}

/// <summary>zeronova-lead's <c>ActivityChannel</c> names this service sends.</summary>
public static class ActivityChannels
{
    public const string Website = "Website";
    public const string Call = "Call";
    public const string WhatsApp = "WhatsApp";
    public const string Internal = "Internal";
}

/// <summary>
/// The capture metadata keys this service writes (and reads back on <c>lead-enquiry-received</c>). zeronova-lead passes
/// them on untouched; zeronova-cms's website forms send <c>form</c>, <c>product</c>, <c>propertyId</c>, <c>projectId</c>.
/// </summary>
public static class EnquiryMetadata
{
    public const string Product = "product";

    /// <summary>"1" on every enquiry this service captured itself.</summary>
    public const string Estate = "estate";

    public const string Form = "form";
    public const string PropertyId = "propertyId";
    public const string ProjectId = "projectId";
    public const string UnitTypeId = "unitTypeId";
    public const string ShareToken = "shareToken";
    public const string RequirementId = "requirementId";

    /// <summary>Our own captures: the <c>EnquirySource</c> name (Call, WalkIn, WhatsApp, Other; AiOffice from C6).</summary>
    public const string Source = "estateSource";

    /// <summary>Our own captures: the agent who works the enquiry (wins over the lead's owner).</summary>
    public const string AgentId = "agentId";

    public const string AgentName = "agentName";

    /// <summary>zeronova-lead reads it (website host); never an extra field.</summary>
    public const string Site = "site";

    public const string ProductRealEstate = "REAL_ESTATE";
    public const string SharePageForm = "share-page";
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
