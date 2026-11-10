using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Models.Requirements;

/// <summary>The demand report's dimensions (D34). A row counts in every group its criteria fall in (two localities = two groups).</summary>
public enum DemandGroupBy
{
    Locality,
    Type,
    Bhk,
    BudgetBand,
}

/// <summary>
/// Where unmet demand comes from (D34): (a) watched requirements, (b) active ones with no match for days, (c) requirements
/// closed not available (the watch over, or closed before C3), (d) enquiries closed not available, (e) website searches
/// that found nothing.
/// </summary>
public enum DemandSource
{
    Watching,
    Unmatched,
    ClosedNotAvailable,
    EnquiryNotAvailable,
    WebsiteSearch,
}

/// <summary>A customer behind a group (sources a–d only; never for a website search, which knows nobody).</summary>
public sealed record DemandSampleDto(DemandSource Source, string Id, string? Code, string? Name, string? LeadId, string? ContactId, string? AgentId, string What);

public sealed record DemandSourceCountDto(DemandSource Source, int Count);

/// <summary>
/// One group, always within a purpose (sale and rent demand never mix; null = a website search that did not say): the values
/// of the asked dimensions (null = that dimension not asked; <c>Any</c> = the demand did not say).
/// <see cref="Type"/> is a <see cref="PropertyType"/> name, <see cref="Bhk"/> a number, <see cref="BudgetBand"/> a band
/// such as <c>50L-1Cr</c> (sale) or <c>25k-50k</c> (rent, a month). Counts per source (website searches are counted
/// searches, not people), and up to five sample customers.
/// </summary>
public sealed record DemandGroupDto(
    PropertyPurpose? Purpose,
    string? Locality,
    string? Type,
    string? Bhk,
    string? BudgetBand,
    int Total,
    IReadOnlyList<DemandSourceCountDto> BySource,
    IReadOnlyList<DemandSampleDto> Samples);

/// <summary>
/// The unmet-demand report (D34) over India days <see cref="From"/> to <see cref="To"/> (both included), biggest groups
/// first. <see cref="UnmatchedDays"/>: how long an active requirement had no match to count as unmatched.
/// </summary>
public sealed record DemandReportDto(
    string From,
    string To,
    IReadOnlyList<DemandGroupBy> GroupBy,
    int UnmatchedDays,
    IReadOnlyList<DemandSourceCountDto> Totals,
    IReadOnlyList<DemandGroupDto> Groups);
