using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Money;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Visits;

namespace ZeroNovaRealEstate.Models.Customers;

/// <summary>
/// The Customers list's filter (C2, D32). <see cref="Text"/> matches names and phones; <see cref="AgentId"/> works for a
/// manager (PRQV) only; the last-activity window is from (included) to (excluded).
/// </summary>
public sealed class EstateCustomerFilterInput
{
    public string? Text { get; init; }
    public string? AgentId { get; init; }
    public bool? HasOpenRequirement { get; init; }

    /// <summary>A new match on an active requirement within the last 7 days (C3 adds revived matches).</summary>
    public bool? Highlighted { get; init; }

    public DateTimeOffset? LastActivityFrom { get; init; }
    public DateTimeOffset? LastActivityTo { get; init; }
}

/// <summary>A customer's numbers as the caller may see them: an agent's own share (PRQM), everyone's with PRQV. Deals need PDLV / PDLM.</summary>
public sealed record EstateCustomerCountsDto(int Enquiries, int OpenRequirements, int Visits, int? Deals);

/// <summary>
/// A row of the Customers list: a zeronova-lead contact (or, until an enquiry names the contact, a lead) with anything in Real
/// Estate. The lead side (status, history, activities) the app reads from zeronova-lead itself.
/// </summary>
public sealed record EstateCustomerDto(
    string? ContactId,
    IReadOnlyList<string> LeadIds,
    string? Name,
    string? Phone,
    string? AgentId,
    string? AgentName,
    EstateCustomerCountsDto Counts,
    DateTimeOffset? LastActivityAt,
    bool Highlighted,
    int Unmet);

public sealed record EstateCustomerPageDto(int Total, IReadOnlyList<EstateCustomerDto> Items);

public sealed record CustomerMatchCountsDto(int New, int Shown, int Interested, int Rejected);

public sealed record CustomerStageCountDto(DealStage Stage, int Count);

/// <summary>The numbers on top of the customer's dashboard. Null = the caller may not see that kind (deals, tenancies, owned properties).</summary>
public sealed record EstateCustomerNumbersDto(
    int Enquiries,
    int OpenEnquiries,
    int RequirementsActive,
    int RequirementsClosed,
    CustomerMatchCountsDto Matches,
    int SharesSent,
    int SharesOpened,
    int VisitsDone,
    int VisitsUpcoming,
    int? Deals,
    IReadOnlyList<CustomerStageCountDto>? DealsByStage,
    int? Tenancies,
    int? PropertiesOwned,
    int Unmet);

/// <summary>A requirement on the dashboard: the requirement, how many matches it has in all, and its criteria in words.</summary>
public sealed record CustomerRequirementDto(RequirementDto Requirement, int Matches, string Summary);

/// <summary>A link sent to the customer (a property's or a project's) and how often it was opened.</summary>
public sealed record CustomerShareDto(
    string Id,
    string Url,
    string? PropertyId,
    string? PropertyCode,
    string? PropertyTitle,
    string? ProjectId,
    string? ProjectName,
    string? RequirementId,
    int Opens,
    DateTimeOffset? LastOpenedAt,
    DateTimeOffset CreatedAt,
    int Enquiries);

/// <summary>A property the customer owns (as a party) — seen only with POWV.</summary>
public sealed record CustomerOwnedPropertyDto(
    string PropertyId,
    string Code,
    string Title,
    PropertyPurpose Purpose,
    PropertyStatus Status,
    string PartyId,
    decimal SharePercent,
    HoldingKind Holding);

public enum CustomerUnmetKind
{
    Requirement,
    Enquiry,
}

/// <summary>
/// What the customer asked for and did not get (D32): a requirement closed as not available or lost (<see cref="What"/> =
/// its criteria), or an enquiry closed as not available (<see cref="What"/> = what was wanted). C3 adds watching.
/// </summary>
public sealed record CustomerUnmetDto(
    CustomerUnmetKind Kind,
    string Id,
    string? Code,
    string What,
    string? Reason,
    DateTimeOffset? ClosedAt,
    string? AgentId,
    RequirementStatus? RequirementStatus,
    EnquiryCloseReason? EnquiryCloseReason);

/// <summary>
/// The Real Estate side of a customer's dashboard (D32), as the caller may see it: enquiries, requirements, matches, shares
/// and visits are an agent's own (PRQM) or everyone's (PRQV); deals need PDLV / PDLM (own with PDLM only), tenancies PRNV /
/// PRNM (own with PRNM only), owned properties POWV — a list the caller may not see is null. The contact, its leads and
/// activities come from zeronova-lead, read by the app.
/// </summary>
public sealed record EstateCustomerDashboardDto(
    string? ContactId,
    IReadOnlyList<string> LeadIds,
    string? Name,
    string? Phone,
    bool Highlighted,
    DateTimeOffset? LastActivityAt,
    EstateCustomerNumbersDto Numbers,
    IReadOnlyList<EnquiryDto> Enquiries,
    IReadOnlyList<CustomerRequirementDto> Requirements,
    IReadOnlyList<MatchDto> Matches,
    IReadOnlyList<CustomerShareDto> Shares,
    IReadOnlyList<VisitDto> Visits,
    IReadOnlyList<DealDto>? Deals,
    IReadOnlyList<TenancyDto>? Tenancies,
    IReadOnlyList<CustomerOwnedPropertyDto>? OwnedProperties,
    IReadOnlyList<CustomerUnmetDto> Unmet);
