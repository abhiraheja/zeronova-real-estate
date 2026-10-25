using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Models.Requirements;

public enum RequirementStatus
{
    Active,
    OnHold,
    Closed,
    Lost,
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
    DateTimeOffset? CreatedAt);

public sealed record CloseRequirementInput(RequirementStatus Status, string? Reason);

public enum MatchStatus
{
    New,
    Shown,
    Interested,
    Rejected,
}

/// <summary>
/// A property that fits a requirement (D10), with its score (0–100) and why. Once shown or rejected it is never offered
/// again; <see cref="PriceWhenMatched"/> lets a later price drop be told apart.
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
    DateTimeOffset CreatedAt);

public sealed record MatchUpdateInput(MatchStatus Status, string? RejectReason);
