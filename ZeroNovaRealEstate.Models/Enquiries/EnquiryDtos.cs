namespace ZeroNovaRealEstate.Models.Enquiries;

/// <summary>How an enquiry came (D27). <see cref="WebsiteForm"/> keeps the form's name beside it.</summary>
public enum EnquirySource
{
    WebsiteForm,
    SharePage,
    WhatsApp,
    AiOffice,
    Call,
    WalkIn,
    Portal,
    Other,
}

/// <summary>new → contacted → requirement added / visit booked → closed (D27, D31). Closing never touches the lead.</summary>
public enum EnquiryStatus
{
    New,
    Contacted,
    RequirementAdded,
    VisitBooked,
    Closed,
}

/// <summary>Why an enquiry was closed (D31). <see cref="NotAvailable"/> keeps what was wanted (C3 watches it).</summary>
public enum EnquiryCloseReason
{
    NotAvailable,
    NotInterested,
    Unreachable,
    Duplicate,
    Spam,
    Converted,
}

/// <summary>The stats' buckets: India days, weeks from Monday, calendar months.</summary>
public enum EnquiryBucket
{
    Day,
    Week,
    Month,
}

/// <summary>A form's extra field (budget, BHK, slot …) as the website sent it.</summary>
public sealed record EnquiryFieldDto(string Key, string Value);

public sealed record EnquirySourceCountDto(EnquirySource Source, int Count);

/// <summary>
/// One person asking about something (D27): a property, a project (and unit type) or a general need, from any source,
/// with the lead and contact it reached in zeronova-lead. The titles are as they were when it came.
/// </summary>
public sealed record EnquiryDto(
    string Id,
    string Code,
    DateTimeOffset OccurredAt,
    EnquirySource Source,
    string? Form,
    string? LeadId,
    string? ContactId,
    bool IsNewLead,
    string? Name,
    string? Phone,
    string? Email,
    string? PropertyId,
    string? PropertyCode,
    string? PropertyTitle,
    string? ProjectId,
    string? ProjectName,
    string? UnitTypeId,
    string? UnitTypeName,
    string? Message,
    IReadOnlyList<EnquiryFieldDto> Extra,
    string? AgentId,
    string? AgentName,
    EnquiryStatus Status,
    EnquiryCloseReason? CloseReason,
    string? Wanted,
    string? CloseNote,
    string? RequirementId,
    string? VisitId,
    string Ref,
    string? SourceUrl,
    DateTimeOffset? ContactedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record EnquiryPageDto(int Total, IReadOnlyList<EnquiryDto> Items);

/// <summary>
/// The Enquiries screen's filters (D30). <see cref="From"/> inclusive, <see cref="To"/> exclusive (instants).
/// <see cref="Text"/> searches the name, phone, email, code and the listing's title.
/// </summary>
public sealed class EnquiryFilterInput
{
    public EnquirySource? Source { get; init; }
    public EnquiryStatus? Status { get; init; }
    public string? PropertyId { get; init; }
    public string? ProjectId { get; init; }
    public string? UnitTypeId { get; init; }

    /// <summary>Only with PRQV; an agent always sees their own.</summary>
    public string? AgentId { get; init; }

    public string? LeadId { get; init; }
    public string? ContactId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? Text { get; init; }
}

/// <summary>
/// "Add enquiry" (D29): an existing lead, or a new person by name and phone; what they asked about; how they came (call,
/// walk-in, WhatsApp or other); the agent (empty = the caller; another agent only with PRQV).
/// </summary>
public sealed class AddEnquiryInput
{
    public string? LeadId { get; init; }
    public string? Name { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? PropertyId { get; init; }
    public string? ProjectId { get; init; }
    public string? UnitTypeId { get; init; }
    public EnquirySource Source { get; init; } = EnquirySource.Call;
    public string? Message { get; init; }
    public string? AgentId { get; init; }
    public string? AgentName { get; init; }
}

/// <summary>
/// The enquiry is on its way: zeronova-lead records it on the lead and announces it, and the row appears here then. Poll
/// <c>ZeroNova_estateEnquiryByRef</c> with <see cref="Ref"/>.
/// </summary>
public sealed record EnquiryAckDto(string Ref, DateTimeOffset SentAt);

/// <summary>The share page's "Enquire" answer.</summary>
public sealed record ShareEnquiryAckDto(bool Received, string Message);

/// <summary>
/// Working an enquiry (D31). Null keeps. <see cref="Status"/> Closed needs <see cref="CloseReason"/>; NotAvailable needs
/// <see cref="Wanted"/>. A linked requirement or visit moves it on (requirement added / visit booked). Another agent
/// only with PRQV.
/// </summary>
public sealed class UpdateEnquiryInput
{
    public EnquiryStatus? Status { get; init; }
    public EnquiryCloseReason? CloseReason { get; init; }
    public string? Wanted { get; init; }
    public string? CloseNote { get; init; }
    public string? RequirementId { get; init; }
    public string? VisitId { get; init; }
    public string? AgentId { get; init; }
    public string? AgentName { get; init; }
}

/// <summary>Enquiries on a property, a project or a unit type (D30): all time, the last 7 and 30 days, and by source. Spam is left out.</summary>
public sealed record EnquiryCountsDto(int Total, int Last7Days, int Last30Days, IReadOnlyList<EnquirySourceCountDto> BySource)
{
    public static readonly EnquiryCountsDto None = new(0, 0, 0, []);
}

/// <summary>One bucket: its first India day (yyyy-MM-dd), the enquiries in it, by source, and how many are still open or closed now.</summary>
public sealed record EnquiryBucketDto(string Start, int Total, IReadOnlyList<EnquirySourceCountDto> BySource, int Open, int Closed);

/// <summary>Day / week / month views (D30) over India days <see cref="From"/> to <see cref="To"/> (both included).</summary>
public sealed record EnquiryStatsDto(
    EnquiryBucket Bucket, string From, string To, int Total, IReadOnlyList<EnquirySourceCountDto> BySource, int Open, int Closed,
    IReadOnlyList<EnquiryBucketDto> Buckets);
