using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Models.Requirements;

public enum RequirementStatus
{
    Active,
    OnHold,
    Closed,
    Lost,

    /// <summary>
    /// Closed as not available and watched until <see cref="RequirementDto.WatchUntil"/> (D33): still matched, and a new fit
    /// is a revived match. The daily sweep closes it when the date passes; it can be reopened to active.
    /// </summary>
    Watching,
}

/// <summary>
/// Why a requirement was closed (C3). <see cref="NotAvailable"/> watches it (D33). The free-text reason stays beside it as a
/// note. A row closed before C3 has none (its note is read the old way: "not available" / "nahi mila").
/// </summary>
public enum RequirementCloseReason
{
    BoughtThroughUs,
    RentedThroughUs,
    BoughtElsewhere,
    RentedElsewhere,
    NotAvailable,
    NotInterested,
    Unreachable,
    BudgetMismatch,
    Other,
}

public enum FloorPreference
{
    Any,
    Ground,
    NotTop,
    Top,
}

/// <summary>
/// What a buyer or tenant wants (design 4.4), saved even when nothing matches (D9). Empty lists mean "any". The lead's
/// name and phone travel with it for display; the lead itself stays in zeronova-lead (D3).
/// </summary>
public sealed class RequirementInput
{
    public string LeadId { get; init; } = string.Empty;

    /// <summary>The lead's contact (the lead picker's <c>contactId</c>): the customer's key (C2). Empty keeps the stored one for the same lead.</summary>
    public string? ContactId { get; init; }

    public string? LeadName { get; init; }
    public string? LeadPhone { get; init; }

    /// <summary>The member (ZeroNova user id) who works this customer; empty = the caller.</summary>
    public string? AgentId { get; init; }

    public PropertyPurpose Purpose { get; init; }
    public IReadOnlyList<PropertyType>? Types { get; init; }
    public string? City { get; init; }

    /// <summary>In order of preference; the first scores highest.</summary>
    public IReadOnlyList<string>? Localities { get; init; }

    public decimal? BudgetMin { get; init; }
    public decimal? BudgetMax { get; init; }
    public AreaInput? SizeMin { get; init; }
    public AreaInput? SizeMax { get; init; }
    public IReadOnlyList<int>? Bhk { get; init; }
    public IReadOnlyList<Facing>? Facings { get; init; }
    public bool CornerOnly { get; init; }
    public bool ParkFacingOnly { get; init; }
    public FloorPreference Floor { get; init; } = FloorPreference.Any;
    public bool NeedsLift { get; init; }
    public bool NeedsParking { get; init; }
    public IReadOnlyList<Furnishing>? Furnishings { get; init; }

    /// <summary>For rent: who will live there.</summary>
    public TenantPreference? TenantType { get; init; }

    /// <summary>yyyy-MM-dd.</summary>
    public string? NeededBy { get; init; }

    public string? Notes { get; init; }

    /// <summary>The enquiry this requirement answers (D31): it becomes "requirement added" and links here.</summary>
    public string? EnquiryId { get; init; }
}

public sealed record RequirementDto(
    string Id,
    string LeadId,
    string? LeadName,
    string? LeadPhone,
    string AgentId,
    RequirementStatus Status,
    string? CloseReason,
    PropertyPurpose Purpose,
    IReadOnlyList<PropertyType> Types,
    string? City,
    IReadOnlyList<string> Localities,
    decimal? BudgetMin,
    decimal? BudgetMax,
    AreaDto? SizeMin,
    AreaDto? SizeMax,
    IReadOnlyList<int> Bhk,
    IReadOnlyList<Facing> Facings,
    bool CornerOnly,
    bool ParkFacingOnly,
    FloorPreference Floor,
    bool NeedsLift,
    bool NeedsParking,
    IReadOnlyList<Furnishing> Furnishings,
    TenantPreference? TenantType,
    string? NeededBy,
    string? Notes,
    int NewMatches,
    DateTimeOffset? CreatedAt,
    string? ContactId = null,
    RequirementCloseReason? CloseReasonCode = null,
    DateTimeOffset? ClosedAt = null,
    DateTimeOffset? WatchUntil = null,
    string? EnquiryId = null);

/// <summary>
/// Close (with <see cref="ReasonCode"/>), lose, pause, watch or reopen a requirement. <see cref="Reason"/> is the free-text
/// note. Closing (or losing) as <see cref="RequirementCloseReason.NotAvailable"/> — or asking for
/// <see cref="RequirementStatus.Watching"/> — watches it for the dealer's watch days (D33). A close without a code is read as
/// before: a note saying not available ("not available", "nahi mila") is NotAvailable, anything else Other.
/// </summary>
public sealed record CloseRequirementInput(RequirementStatus Status, string? Reason, RequirementCloseReason? ReasonCode = null);

public enum MatchStatus
{
    New,
    Shown,
    Interested,
    Rejected,
}

/// <summary>
/// A property that fits a requirement (D10), with its score (0–100) and why. Once shown or rejected it is never offered
/// again; <see cref="PriceWhenMatched"/> lets a later price drop be told apart. A match to a project's unit type (D24)
/// carries the project and type and points at the best available unit; <see cref="AvailableInGroup"/> is how many
/// available units of the type fit. <see cref="Revived"/>: made while the requirement was watched (D33) — the customer asked
/// for this earlier and it is available now.
/// </summary>
public sealed record MatchDto(
    string Id,
    string PropertyId,
    string PropertyCode,
    string PropertyTitle,
    decimal? PropertyPrice,
    PropertyStatus PropertyStatus,
    string RequirementId,
    string LeadId,
    string? LeadName,
    string? LeadPhone,
    string AgentId,
    int Score,
    IReadOnlyList<string> Reasons,
    MatchStatus Status,
    string? RejectReason,
    decimal? PriceWhenMatched,
    DateTimeOffset CreatedAt,
    string? ProjectId = null,
    string? ProjectName = null,
    string? UnitTypeId = null,
    string? UnitTypeName = null,
    int? AvailableInGroup = null,
    bool Revived = false,
    MatchMessageVia? MessageVia = null,
    DateTimeOffset? MessagedAt = null,
    string? MessageFailed = null);

/// <summary>How the customer was told of a match (D33): the company's WhatsApp template, or a wa.me draft the agent opened.</summary>
public enum MatchMessageVia
{
    WhatsAppTemplate,
    Draft,
}

/// <summary>
/// The one-click "now available" message (D33). <see cref="Sent"/>: handed to zeronova-social with the company's approved
/// template (<see cref="MatchDto.MessageFailed"/> says later if WhatsApp refused it). Otherwise <see cref="DraftUrl"/> is
/// the ready wa.me message for the app to open (null when the customer has no phone). The share link is fresh either way,
/// and the match is now shown.
/// </summary>
public sealed record MatchMessageDto(bool Sent, MatchMessageVia Via, string? DraftUrl, string ShareUrl, string Text, string? Phone, MatchDto Match);

public sealed record MatchUpdateInput(MatchStatus Status, string? RejectReason);
