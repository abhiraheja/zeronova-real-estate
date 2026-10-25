using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Models.Settings;

/// <summary>
/// The dealer's settings (design 4.2): the local size of the regional units, how far over budget to match, when a
/// property goes stale, the match threshold, the default city, the RERA number, and the name and phone customers see on
/// a shared property.
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
    string? DealerPhone);

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
    string? DealerPhone = null);

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

/// <summary>The Real Estate dashboard (design 3, M1): counts, then the newest matches and the oldest stale properties.</summary>
public sealed record EstateDashboardDto(
    int AvailableForSale,
    int AvailableForRent,
    int Stale,
    int ActiveRequirements,
    int NewMatches,
    int AddedThisWeek,
    IReadOnlyList<MatchDto> LatestMatches,
    IReadOnlyList<PropertyDto> OldestStale);
