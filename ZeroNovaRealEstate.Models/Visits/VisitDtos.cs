using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Models.Visits;

public enum VisitStatus
{
    Scheduled,
    Done,
    NoShow,
    Cancelled,
}

/// <summary>What the customer thought after seeing the property; it moves the match (interested, rejected).</summary>
public enum VisitOutcome
{
    Interested,
    Negotiating,
    Thinking,
    NotInterested,
}

/// <summary>A site visit to schedule (M2): the property, the customer (lead, and their requirement when there is one), when.</summary>
public sealed class VisitInput
{
    public string PropertyId { get; init; } = string.Empty;
    public string? RequirementId { get; init; }
    public string LeadId { get; init; } = string.Empty;
    public string? LeadName { get; init; }
    public string? LeadPhone { get; init; }

    /// <summary>The member who takes the customer; empty = the caller.</summary>
    public string? AgentId { get; init; }

    public DateTimeOffset At { get; init; }
    public string? MeetingPoint { get; init; }
    public string? Notes { get; init; }
}

public sealed record VisitResultInput(VisitStatus Status, VisitOutcome? Outcome, string? Feedback);

public sealed record VisitDto(
    string Id,
    string PropertyId,
    string PropertyCode,
    string PropertyTitle,
    string? RequirementId,
    string LeadId,
    string? LeadName,
    string? LeadPhone,
    string AgentId,
    DateTimeOffset At,
    string? MeetingPoint,
    VisitStatus Status,
    VisitOutcome? Outcome,
    string? Feedback,
    string? Notes,
    KeysWith? KeysWith,
    string? VisitTimings,
    string? OwnerPhone,
    string ClientMessage,
    string? OwnerMessage);

/// <summary>A link to send a property to a customer (M2): a page with photos and details, never the owner or the unit number.</summary>
public sealed record ShareDto(string Id, string Token, string Url, string PropertyId, string? RequirementId, int Opens, DateTimeOffset? LastOpenedAt,
    DateTimeOffset CreatedAt, string Message);

/// <summary>What the customer sees on the shared page.</summary>
public sealed record SharedPropertyDto(
    string Title,
    PropertyPurpose Purpose,
    PropertyType Type,
    string? City,
    string Locality,
    string? Society,
    AreaDto? PlotArea,
    AreaDto? BuiltUpArea,
    AreaDto? CarpetArea,
    AreaDto? SuperArea,
    decimal? Price,
    bool Negotiable,
    decimal? Rent,
    decimal? Deposit,
    int? Bhk,
    int? Bathrooms,
    int? Floor,
    int? TotalFloors,
    Furnishing? Furnishing,
    Facing? Facing,
    bool Corner,
    bool ParkFacing,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<PhotoDto> Photos,
    string? VideoUrl,
    string? DealerName,
    string? DealerPhone,
    string? ReraNumber,
    bool StillAvailable);

/// <summary>A ready WhatsApp message (M2) — the app opens it in WhatsApp (wa.me) from the dealer's own phone.</summary>
public sealed record WhatsAppDraftDto(string? Phone, string Text, string? Link);
