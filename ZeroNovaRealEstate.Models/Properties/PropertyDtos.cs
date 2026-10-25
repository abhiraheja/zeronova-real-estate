namespace ZeroNovaRealEstate.Models.Properties;

public enum PropertyPurpose
{
    Sale,
    Rent,
}

/// <summary>What the property is (design D4). Built types carry BHK, floors and furnishing; land types carry facing and corner.</summary>
public enum PropertyType
{
    Plot,
    Flat,
    BuilderFloor,
    House,
    Villa,
    Penthouse,
    Shop,
    Office,
    Showroom,
    Warehouse,
    AgriculturalLand,
    IndustrialPlot,
    Pg,
}

/// <summary>Where a property stands (D6). Only <see cref="Available"/> (and a lapsed hold) is matched and sent to customers.</summary>
public enum PropertyStatus
{
    Available,
    OnHold,
    UnderNegotiation,
    TokenReceived,
    Sold,
    Rented,
    Withdrawn,
}

/// <summary>
/// Area units (D5). Fixed ones convert by a fixed factor; marla, kanal, bigha, biswa and katha take the dealer's local
/// value from settings. <see cref="SqYd"/> is the gaj.
/// </summary>
public enum AreaUnit
{
    SqFt,
    SqYd,
    SqM,
    Marla,
    Kanal,
    Bigha,
    Biswa,
    Katha,
    Acre,
    Guntha,
    Cent,
    Hectare,
}

public enum Facing
{
    North,
    East,
    South,
    West,
    NorthEast,
    NorthWest,
    SouthEast,
    SouthWest,
}

public enum Furnishing
{
    Unfurnished,
    SemiFurnished,
    FullyFurnished,
}

public enum ConstructionStatus
{
    Ready,
    UnderConstruction,
}

/// <summary>How the land is held (plots): freehold, leasehold, or a power of attorney (GPA).</summary>
public enum LandTenure
{
    Freehold,
    Leasehold,
    PowerOfAttorney,
}

/// <summary>Where the property came from: its owner directly, another dealer, or the dealer's own.</summary>
public enum PropertySource
{
    Owner,
    OtherDealer,
    Own,
}

public enum KeysWith
{
    Owner,
    Dealer,
    Guard,
    Tenant,
    Other,
}

public enum MandateKind
{
    Open,
    Exclusive,
}

public enum Occupancy
{
    Vacant,
    OwnerOccupied,
    TenantOccupied,
}

public enum TenantPreference
{
    Family,
    Bachelors,
    Company,
}

/// <summary>How the dealer last confirmed the property is still as listed (D6).</summary>
public enum ConfirmedVia
{
    Added,
    Call,
    WhatsApp,
    Visit,
}

/// <summary>A size as entered: a value and its unit. Searched in sq ft (D5).</summary>
public sealed record AreaInput(decimal Value, AreaUnit Unit);

public sealed record AreaDto(decimal Value, AreaUnit Unit, decimal SqFt);

/// <summary>A photo already uploaded to zeronova-document-helper (the public bucket): its storage key and address.</summary>
public sealed record PhotoInput(string Key, string Url);

public sealed record PhotoDto(string Key, string Url);

/// <summary>
/// A property as the dealer enters it (section 4.1 of the design). Only purpose, type and locality are required; the
/// rest is filled as known. Fields for another type are ignored on save (a plot keeps no BHK).
/// </summary>
public sealed class PropertyInput
{
    public PropertyPurpose Purpose { get; init; }
    public PropertyType Type { get; init; }

    // ── Where
    public string? City { get; init; }
    public string Locality { get; init; } = string.Empty;
    public string? Society { get; init; }
    public string? Block { get; init; }

    /// <summary>Plot, house or flat number — seen only with POWV.</summary>
    public string? UnitNumber { get; init; }

    public double? Latitude { get; init; }
    public double? Longitude { get; init; }

    // ── Size
    public AreaInput? PlotArea { get; init; }
    public AreaInput? BuiltUpArea { get; init; }
    public AreaInput? CarpetArea { get; init; }
    public AreaInput? SuperArea { get; init; }
    public decimal? FrontFeet { get; init; }
    public decimal? DepthFeet { get; init; }

    // ── Price (sale)
    public decimal? Price { get; init; }
    public bool Negotiable { get; init; } = true;

    /// <summary>What the owner will really take — seen only with POWV.</summary>
    public decimal? OwnerExpectation { get; init; }

    // ── Price (rent)
    public decimal? Rent { get; init; }
    public decimal? Deposit { get; init; }
    public decimal? Maintenance { get; init; }
    public int? LockInMonths { get; init; }
    public int? NoticeMonths { get; init; }

    // ── Built property
    public int? Bhk { get; init; }
    public int? Bathrooms { get; init; }
    public int? Balconies { get; init; }

    /// <summary>0 = ground, -1 = basement.</summary>
    public int? Floor { get; init; }

    public int? TotalFloors { get; init; }
    public bool? Lift { get; init; }
    public int? CoveredParking { get; init; }
    public int? OpenParking { get; init; }
    public Furnishing? Furnishing { get; init; }
    public int? AgeYears { get; init; }
    public ConstructionStatus? Construction { get; init; }

    /// <summary>yyyy-MM-dd.</summary>
    public string? PossessionDate { get; init; }

    public bool ServantRoom { get; init; }
    public bool PoojaRoom { get; init; }
    public bool StoreRoom { get; init; }
    public bool StudyRoom { get; init; }
    public bool? PowerBackup { get; init; }
    public bool? Gated { get; init; }
    public IReadOnlyList<string>? Amenities { get; init; }

    // ── Plot and orientation
    public Facing? Facing { get; init; }
    public bool Corner { get; init; }
    public bool ParkFacing { get; init; }
    public decimal? RoadWidthFeet { get; init; }
    public LandTenure? Tenure { get; init; }
    public string? KhasraNumber { get; init; }

    // ── Commercial
    public int? Washrooms { get; init; }
    public decimal? PowerLoadKw { get; init; }
    public bool? Pantry { get; init; }
    public bool? CurrentlyLeased { get; init; }
    public decimal? LeasedRent { get; init; }

    // ── Rent terms
    /// <summary>yyyy-MM-dd.</summary>
    public string? AvailableFrom { get; init; }

    public IReadOnlyList<TenantPreference>? TenantPreferences { get; init; }
    public bool? PetsAllowed { get; init; }

    // ── Papers
    public bool? Registry { get; init; }
    public bool? Mutation { get; init; }
    public bool? LoanApproved { get; init; }
    public bool? Noc { get; init; }
    public bool? OccupancyCertificate { get; init; }
    public bool? PropertyTaxPaid { get; init; }

    // ── Source, keys, mandate
    public PropertySource Source { get; init; } = PropertySource.Owner;
    public string? SourceDealerName { get; init; }
    public string? SourceDealerPhone { get; init; }
    public KeysWith? KeysWith { get; init; }
    public string? VisitTimings { get; init; }
    public MandateKind Mandate { get; init; } = MandateKind.Open;

    /// <summary>yyyy-MM-dd.</summary>
    public string? MandateUntil { get; init; }

    public Occupancy? Occupancy { get; init; }

    // ── Media and notes
    public IReadOnlyList<PhotoInput>? Photos { get; init; }
    public string? VideoUrl { get; init; }
    public string? Notes { get; init; }

    /// <summary>The dealer's own fields (D4): key → value as text.</summary>
    public IReadOnlyList<CustomValueInput>? Custom { get; init; }
}

public sealed record CustomValueInput(string Key, string Value);

public sealed record CustomValueDto(string Key, string Label, string Value);

/// <summary>A current owner as listed on the property: the party, their share and how they hold it.</summary>
public sealed record PropertyOwnerDto(string PartyId, string Name, string? Phone, decimal SharePercent, HoldingKind Holding);

/// <summary>How an owner holds the property (design 4.3).</summary>
public enum HoldingKind
{
    BuilderAllotment,
    AuthorityAllotment,
    RegisteredSaleDeed,
    PowerOfAttorney,
    Inheritance,
    Other,
}

public sealed record PropertyOwnerInput(string PartyId, decimal? SharePercent, HoldingKind Holding);

/// <summary>
/// A property as members see it. Owner details (<see cref="UnitNumber"/>, <see cref="OwnerExpectation"/>, owners'
/// phones, the source dealer's phone) are null without POWV.
/// </summary>
public sealed record PropertyDto(
    string Id,
    string Code,
    string Title,
    PropertyPurpose Purpose,
    PropertyType Type,
    PropertyStatus Status,
    string? HoldUntil,
    bool Stale,
    DateTimeOffset LastConfirmedAt,
    ConfirmedVia ConfirmedVia,
    string? City,
    string Locality,
    string? Society,
    string? Block,
    string? UnitNumber,
    double? Latitude,
    double? Longitude,
    AreaDto? PlotArea,
    AreaDto? BuiltUpArea,
    AreaDto? CarpetArea,
    AreaDto? SuperArea,
    decimal? SizeSqFt,
    decimal? FrontFeet,
    decimal? DepthFeet,
    decimal? Price,
    bool Negotiable,
    decimal? OwnerExpectation,
    decimal? RatePerSqFt,
    decimal? Rent,
    decimal? Deposit,
    decimal? Maintenance,
    int? LockInMonths,
    int? NoticeMonths,
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
    string? KhasraNumber,
    int? Washrooms,
    decimal? PowerLoadKw,
    bool? Pantry,
    bool? CurrentlyLeased,
    decimal? LeasedRent,
    string? AvailableFrom,
    IReadOnlyList<TenantPreference> TenantPreferences,
    bool? PetsAllowed,
    bool? Registry,
    bool? Mutation,
    bool? LoanApproved,
    bool? Noc,
    bool? OccupancyCertificate,
    bool? PropertyTaxPaid,
    PropertySource Source,
    string? SourceDealerName,
    string? SourceDealerPhone,
    KeysWith? KeysWith,
    string? VisitTimings,
    MandateKind Mandate,
    string? MandateUntil,
    Occupancy? Occupancy,
    IReadOnlyList<PhotoDto> Photos,
    string? VideoUrl,
    string? Notes,
    IReadOnlyList<CustomValueDto> Custom,
    IReadOnlyList<PropertyOwnerDto> Owners,
    IReadOnlyList<Deals.OwnershipRecordDto> History,
    int OpenMatches,
    DateTimeOffset? CreatedAt);

public enum PropertySort
{
    Newest,
    PriceLow,
    PriceHigh,
    SizeLarge,
    LastConfirmed,
}

/// <summary>
/// The search (design 4.7). Lists left empty mean "any"; <see cref="Statuses"/> empty means available and on hold.
/// <see cref="Custom"/> filters the dealer's yes/no and choice fields by exact value.
/// </summary>
public sealed class PropertySearchInput
{
    public PropertyPurpose? Purpose { get; init; }
    public IReadOnlyList<PropertyType>? Types { get; init; }
    public IReadOnlyList<PropertyStatus>? Statuses { get; init; }
    public string? City { get; init; }
    public IReadOnlyList<string>? Localities { get; init; }
    public decimal? PriceMin { get; init; }
    public decimal? PriceMax { get; init; }
    public decimal? SizeMinSqFt { get; init; }
    public decimal? SizeMaxSqFt { get; init; }
    public IReadOnlyList<int>? Bhk { get; init; }
    public IReadOnlyList<Facing>? Facings { get; init; }
    public bool? Corner { get; init; }
    public bool? ParkFacing { get; init; }
    public IReadOnlyList<Furnishing>? Furnishings { get; init; }

    /// <summary>true = stale only, false = fresh only, null = both.</summary>
    public bool? Stale { get; init; }

    public string? Text { get; init; }
    public IReadOnlyList<CustomValueInput>? Custom { get; init; }
    public PropertySort Sort { get; init; } = PropertySort.Newest;
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
}

public sealed record PropertyPageDto(int TotalCount, IReadOnlyList<PropertyDto> Items);

/// <summary>"Still available?" answered (D6): the status now, a changed price, and how it was confirmed.</summary>
public sealed record ConfirmPropertyInput(PropertyStatus Status, decimal? Price, ConfirmedVia Via, string? HoldUntil);

/// <summary>A property that looks like the one being entered (design 4.6), and why.</summary>
public sealed record DuplicateDto(string Id, string Code, string Title, string Reason);
