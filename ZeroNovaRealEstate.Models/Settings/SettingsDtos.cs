using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Models.Settings;

/// <summary>
/// The dealer's settings (design 4.2): the local size of the regional units, how far over budget to match, when a
/// property goes stale, the match threshold, the default city, the RERA number, and the name and phone customers see on
/// a shared property. C3: how long unmet demand is watched, and the WhatsApp template the one-click message uses (none =
/// a wa.me draft).
/// </summary>
public sealed record EstateSettingsDto(
    decimal MarlaSqFt,
    decimal KanalSqFt,
    decimal BighaSqFt,
    decimal BiswaSqFt,
    decimal KathaSqFt,
    int BudgetTolerancePercent,
    int SaleFreshDays,
    int RentFreshDays,
    int MatchThreshold,
    string? DefaultCity,
    string? ReraNumber,
    string? DealerName,
    string? DealerPhone,
    bool GstRegistered,
    string? Gstin,
    string? DealerAddress,
    decimal SaleBrokeragePercent,
    decimal RentBrokerageMonths,
    int WatchDays = 90,
    string? MatchTemplateName = null,
    string? MatchTemplateLanguage = null);

/// <summary>
/// The settings to save. The C3 fields keep the stored value when null (an app that does not know them yet changes
/// nothing): <see cref="WatchDays"/> — how long a requirement closed as not available is watched (D33, 1–365, default 90);
/// <see cref="MatchTemplateName"/> — the company's approved WhatsApp template (in zeronova-social) for "property now
/// available", empty to stop using one; <see cref="MatchTemplateLanguage"/> — its language code (<c>en</c>, <c>hi</c>).
/// </summary>
public sealed record EstateSettingsInput(
    decimal MarlaSqFt,
    decimal KanalSqFt,
    decimal BighaSqFt,
    decimal BiswaSqFt,
    decimal KathaSqFt,
    int BudgetTolerancePercent,
    int SaleFreshDays,
    int RentFreshDays,
    int MatchThreshold,
    string? DefaultCity,
    string? ReraNumber,
    string? DealerName = null,
    string? DealerPhone = null,
    bool GstRegistered = false,
    string? Gstin = null,
    string? DealerAddress = null,
    decimal SaleBrokeragePercent = 1m,
    decimal RentBrokerageMonths = 1m,
    int? WatchDays = null,
    string? MatchTemplateName = null,
    string? MatchTemplateLanguage = null);

public enum CustomFieldKind
{
    Text,
    Number,
    YesNo,
    Choice,
}

/// <summary>A field the dealer adds (D4): its key is made from the label once and never changes.</summary>
public sealed record CustomFieldDto(
    string Id, string Key, string Label, CustomFieldKind Kind, IReadOnlyList<string> Options, IReadOnlyList<PropertyType> PropertyTypes, bool Filterable);

/// <summary>Empty <see cref="PropertyTypes"/> = every type.</summary>
public sealed record CustomFieldInput(
    string Label, CustomFieldKind Kind, IReadOnlyList<string>? Options, IReadOnlyList<PropertyType>? PropertyTypes, bool Filterable);

/// <summary>A sector, colony or society name the dealer has used, for typing it the same way again.</summary>
public sealed record LocalityDto(string Name, string? City, int Properties);

/// <summary>
/// The Real Estate dashboard (design 3, M1): counts over the dealer's own stock (project units left out), the newest
/// matches, the oldest stale properties, and each project's units by status (design 7). The enquiry counts (C1) are the
/// caller's own unless PRQV, as requirements are: today and this week in India (weeks from Monday), and open now.
/// </summary>
public sealed record EstateDashboardDto(
    int AvailableForSale,
    int AvailableForRent,
    int Stale,
    int ActiveRequirements,
    int NewMatches,
    int AddedThisWeek,
    IReadOnlyList<MatchDto> LatestMatches,
    IReadOnlyList<PropertyDto> OldestStale,
    IReadOnlyList<ZeroNovaRealEstate.Models.Projects.ProjectSummaryDto> Projects,
    int EnquiriesToday = 0,
    int EnquiriesThisWeek = 0,
    int OpenEnquiries = 0);
