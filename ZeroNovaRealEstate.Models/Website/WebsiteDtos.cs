using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Models.Website;

// ── Settings (PSEM) ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The company's website (cms doc 06 §12, E2): on or off, its public code (made on first ask; the admin copies it into
/// the cms setting <c>estateWebsiteCode</c>), and how many listings it shows now.
/// </summary>
public sealed record EstateWebsiteSettingsDto(bool Enabled, string Code, int ListedProperties, int ListedProjects);

/// <summary>Null keeps what is saved. A code: 4 to 20 letters and digits, unique across companies (kept in upper case).</summary>
public sealed record EstateWebsiteSettingsInput(bool? Enabled, string? Code);

// ── The public listings (anonymous, by website code) ──────────────────────────────────────────────────

/// <summary>
/// The company as its share pages show it — name, phone, RERA number — with the cities and localities its listings are
/// in, and how many listings there are.
/// </summary>
public sealed record EstateWebsiteDto(
    string Code,
    string? CompanyName,
    string? Phone,
    string? ReraNumber,
    IReadOnlyList<string> Cities,
    IReadOnlyList<EstateWebsiteLocalityDto> Localities,
    int Properties,
    int ForSale,
    int ForRent,
    int Projects);

/// <summary>A locality in use: its name, a slug (what a "Popular localities" tile searches by), its city and its listings.</summary>
public sealed record EstateWebsiteLocalityDto(string Name, string Slug, string? City, int Properties, int Projects);

public enum EstateWebsitePropertySort
{
    /// <summary>Featured first, then the newest.</summary>
    Featured,
    Newest,
    PriceLow,
    PriceHigh,
    SizeLarge,
}

/// <summary>
/// The listing filters of the website's design (cms doc 06 §12). Everything is optional; lists left empty mean "any".
/// <see cref="Commercial"/> true = shops, offices, showrooms and warehouses only (false = none of them);
/// <see cref="Plots"/> true = plots and land only (false = none). <see cref="Locality"/> takes a locality's name or its
/// slug. <see cref="Construction"/> is the "status" filter: ready to move or under construction. Prices are the sale
/// price, or the monthly rent.
/// </summary>
public sealed class EstateWebsitePropertyFilterInput
{
    public PropertyPurpose? Purpose { get; init; }
    public IReadOnlyList<PropertyType>? Types { get; init; }
    public bool? Commercial { get; init; }
    public bool? Plots { get; init; }
    public string? City { get; init; }
    public string? Locality { get; init; }
    public string? Text { get; init; }
    public decimal? PriceFrom { get; init; }
    public decimal? PriceTo { get; init; }
    public IReadOnlyList<int>? Bhk { get; init; }
    public IReadOnlyList<Furnishing>? Furnishings { get; init; }
    public ConstructionStatus? Construction { get; init; }
    public IReadOnlyList<Facing>? Facings { get; init; }
    public bool? Corner { get; init; }
    public bool? ParkFacing { get; init; }
    public bool? Featured { get; init; }
}

/// <summary>
/// A listed property, as its share page shows it plus the other listing fields: never the owners or their contacts, the
/// unit number, block or map pin, the owner's expectation, the source, keys, mandate, papers, notes or the dealer's own
/// fields. <see cref="Price"/> is null when the price is on request. <see cref="Id"/> is the property's own id (the site may
/// send it back on an enquiry); <see cref="Description"/> is the dealer's public description, never the private notes.
/// </summary>
public sealed record EstateWebsitePropertyDto(
    string Id,
    string Slug,
    string Code,
    string Title,
    PropertyPurpose Purpose,
    PropertyType Type,
    PropertyStatus Status,
    bool Featured,
    string? City,
    string Locality,
    string? Society,
    AreaDto? PlotArea,
    AreaDto? BuiltUpArea,
    AreaDto? CarpetArea,
    AreaDto? SuperArea,
    decimal? SizeSqFt,
    decimal? FrontFeet,
    decimal? DepthFeet,
    decimal? Price,
    bool PriceOnRequest,
    bool Negotiable,
    decimal? Rent,
    decimal? Deposit,
    decimal? Maintenance,
    int? Bhk,
    int? Bathrooms,
    int? Balconies,
    int? Floor,
    int? TotalFloors,
    bool? Lift,
    int? CoveredParking,
    int? OpenParking,
    Furnishing? Furnishing,
    int? AgeYears,
    ConstructionStatus? Construction,
    string? PossessionDate,
    bool ServantRoom,
    bool PoojaRoom,
    bool StoreRoom,
    bool StudyRoom,
    bool? PowerBackup,
    bool? Gated,
    IReadOnlyList<string> Amenities,
    Facing? Facing,
    bool Corner,
    bool ParkFacing,
    decimal? RoadWidthFeet,
    LandTenure? Tenure,
    int? Washrooms,
    bool? Pantry,
    string? AvailableFrom,
    IReadOnlyList<TenantPreference> TenantPreferences,
    bool? PetsAllowed,
    IReadOnlyList<PhotoDto> Photos,
    string? VideoUrl,
    string? Description)
{
    /// <summary>Its ready videos (D36); play one with <c>ZeroNova_estateWebsiteVideoPlayback(code, slug, videoId)</c>.</summary>
    public IReadOnlyList<ZeroNovaRealEstate.Models.Videos.EstatePublicVideoDto> Videos { get; init; } = [];
}

public sealed record EstateWebsitePropertyPageDto(int TotalCount, int Page, int PageSize, IReadOnlyList<EstateWebsitePropertyDto> Items);

/// <summary>What a property's address on the website leads to.</summary>
public enum EstateWebsiteListingState
{
    Listed,

    /// <summary>Sold, rented, withdrawn, taken off the website or removed: the page says "no longer available" and offers similar ones.</summary>
    NoLongerAvailable,

    NotFound,
}

/// <summary>
/// A property page: the property while it is listed, with similar listings. When it has left the site,
/// <see cref="Property"/> is null and <see cref="Similar"/> offers others (<see cref="Title"/> is kept only for a
/// property that was on the website).
/// </summary>
public sealed record EstateWebsitePropertyResultDto(
    EstateWebsiteListingState State,
    string? Title,
    EstateWebsitePropertyDto? Property,
    IReadOnlyList<EstateWebsitePropertyDto> Similar);

/// <summary>
/// A unit type's availability, from its units (never their numbers): sold out with none available; few left with at most
/// <c>max(3, 10% of the type's units)</c> available; available otherwise — also for a type whose units are not made yet.
/// </summary>
public enum EstateWebsiteAvailability
{
    Available,
    FewLeft,
    SoldOut,
}

/// <summary>A project in the website's list: what it is, where, a cover, its "from" price, its unit types' BHK and types.</summary>
public sealed record EstateWebsiteProjectSummaryDto(
    string Id,
    string Slug,
    string Name,
    IReadOnlyList<ProjectCategory> Categories,
    ProjectStage Stage,
    string? City,
    string Locality,
    string? CoverUrl,
    string? LaunchDate,
    string? PossessionDate,
    IReadOnlyList<string> ReraNumbers,
    IReadOnlyList<PropertyType> PropertyTypes,
    IReadOnlyList<int> Bhk,
    decimal? FromPrice,
    EstateWebsiteAvailability Availability,
    bool Featured);

/// <summary>A unit type on the website: its size, its "from" price (null when on request or sold out) and its availability.</summary>
public sealed record EstateWebsiteUnitTypeDto(
    string Name,
    PropertyType Type,
    int? Bhk,
    int? Bathrooms,
    int? Balconies,
    AreaDto? CarpetArea,
    AreaDto? BuiltUpArea,
    AreaDto? SuperArea,
    AreaDto? PlotArea,
    decimal? FrontFeet,
    decimal? DepthFeet,
    PhotoDto? FloorPlan,
    decimal? FromPrice,
    bool PriceOnRequest,
    EstateWebsiteAvailability Availability);

/// <summary>A payment-plan step as text ("10% on booking", "20% at Slab 3 casting"), with its parts.</summary>
public sealed record EstateWebsitePlanStepDto(string Label, decimal? Percent, decimal? Amount, StepDue Due, int? Days, string? MilestoneName, string Text);

public sealed record EstateWebsitePaymentPlanDto(string Name, PaymentPlanKind Kind, IReadOnlyList<EstateWebsitePlanStepDto> Steps);

/// <summary>
/// A listed project, as its share page shows it plus its payment plans as text: never CP terms, the builder's contact,
/// unit numbers, the price list's workings, notes or holds.
/// </summary>
public sealed record EstateWebsiteProjectDto(
    string Id,
    string Slug,
    string Name,
    IReadOnlyList<ProjectCategory> Categories,
    ProjectStage Stage,
    string? LaunchDate,
    string? PossessionDate,
    IReadOnlyList<ReraDto> Reras,
    string? City,
    string Locality,
    string? Address,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<LandmarkDto> Landmarks,
    AreaDto? LandArea,
    IReadOnlyList<TowerDto> Towers,
    IReadOnlyList<KeyFigureDto> KeyFigures,
    string? Overview,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<SpecificationDto> Specifications,
    IReadOnlyList<PhotoDto> Photos,
    string? VideoUrl,
    string? DroneUrl,
    MediaFileDto? Brochure,
    PhotoDto? SitePlan,
    IReadOnlyList<ConstructionUpdateDto> Updates,
    IReadOnlyList<EstateWebsitePaymentPlanDto> PaymentPlans,
    IReadOnlyList<EstateWebsiteUnitTypeDto> UnitTypes,
    decimal? FromPrice,
    EstateWebsiteAvailability Availability,
    bool Featured)
{
    /// <summary>Its ready videos (D36); play one with <c>ZeroNova_estateWebsiteVideoPlayback(code, slug, videoId)</c>.</summary>
    public IReadOnlyList<ZeroNovaRealEstate.Models.Videos.EstatePublicVideoDto> Videos { get; init; } = [];
}
