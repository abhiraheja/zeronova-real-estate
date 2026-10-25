using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ZeroNovaRealEstate.Domain.Base;
using ZeroNovaRealEstate.Domain.Helper;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Domain.Documents;

/// <summary>A size as entered, with its sq ft at the time of saving (D5) — what search and matching compare.</summary>
public sealed class AreaModel
{
    [BsonElement("value")] public decimal Value { get; set; }

    [BsonElement("unit")]
    [BsonRepresentation(BsonType.String)]
    public AreaUnit Unit { get; set; }

    [BsonElement("sqFt")] public decimal SqFt { get; set; }
}

public sealed class PhotoModel
{
    [BsonElement("key")] public required string Key { get; set; }
    [BsonElement("url")] public required string Url { get; set; }
}

/// <summary>A current owner: the party, their share (total 100%) and how they hold the property (design 4.3).</summary>
public sealed class OwnerModel
{
    [BsonElement("partyId")] public required string PartyId { get; set; }
    [BsonElement("share")] public decimal SharePercent { get; set; }

    [BsonElement("holding")]
    [BsonRepresentation(BsonType.String)]
    public HoldingKind Holding { get; set; }
}

/// <summary>
/// A property of the dealer's stock (design 4.1). One record per property, shared by the whole team, with a status and
/// a last-confirmed time everyone sees (D6). <see cref="SizeSqFt"/> and <see cref="PriceValue"/> are the numbers search
/// and matching use: the main size in sq ft, and the sale price or the monthly rent.
/// </summary>
[DocumentName("re_properties")]
public sealed class PropertyDocument : CompanyDocumentBase
{
    /// <summary><c>P-0001</c>, numbered per company.</summary>
    [BsonElement("code")] public required string Code { get; set; }

    [BsonElement("purpose")] [BsonRepresentation(BsonType.String)] public PropertyPurpose Purpose { get; set; }
    [BsonElement("type")] [BsonRepresentation(BsonType.String)] public PropertyType Type { get; set; }
    [BsonElement("status")] [BsonRepresentation(BsonType.String)] public PropertyStatus Status { get; set; } = PropertyStatus.Available;

    /// <summary>yyyy-MM-dd; on hold until then, available again after.</summary>
    [BsonElement("holdUntil")] public string? HoldUntil { get; set; }

    [BsonElement("lastConfirmedAt")] public DateTime LastConfirmedAt { get; set; }
    [BsonElement("confirmedVia")] [BsonRepresentation(BsonType.String)] public ConfirmedVia ConfirmedVia { get; set; }

    [BsonElement("city")] public string? City { get; set; }
    [BsonElement("locality")] public required string Locality { get; set; }

    /// <summary>The locality lower-cased and trimmed — what search and matching compare.</summary>
    [BsonElement("localityKey")] public required string LocalityKey { get; set; }

    [BsonElement("society")] public string? Society { get; set; }
    [BsonElement("block")] public string? Block { get; set; }
    [BsonElement("unitNumber")] public string? UnitNumber { get; set; }
    [BsonElement("lat")] public double? Latitude { get; set; }
    [BsonElement("lng")] public double? Longitude { get; set; }

    [BsonElement("plotArea")] public AreaModel? PlotArea { get; set; }
    [BsonElement("builtUpArea")] public AreaModel? BuiltUpArea { get; set; }
    [BsonElement("carpetArea")] public AreaModel? CarpetArea { get; set; }
    [BsonElement("superArea")] public AreaModel? SuperArea { get; set; }
    [BsonElement("sizeSqFt")] public decimal? SizeSqFt { get; set; }
    [BsonElement("frontFeet")] public decimal? FrontFeet { get; set; }
    [BsonElement("depthFeet")] public decimal? DepthFeet { get; set; }

    [BsonElement("price")] public decimal? Price { get; set; }
    [BsonElement("negotiable")] public bool Negotiable { get; set; } = true;
    [BsonElement("ownerExpectation")] public decimal? OwnerExpectation { get; set; }
    [BsonElement("rent")] public decimal? Rent { get; set; }
    [BsonElement("deposit")] public decimal? Deposit { get; set; }
    [BsonElement("maintenance")] public decimal? Maintenance { get; set; }
    [BsonElement("lockInMonths")] public int? LockInMonths { get; set; }
    [BsonElement("noticeMonths")] public int? NoticeMonths { get; set; }

    /// <summary>The sale price or the monthly rent — what budgets compare against.</summary>
    [BsonElement("priceValue")] public decimal? PriceValue { get; set; }

    [BsonElement("bhk")] public int? Bhk { get; set; }
    [BsonElement("bathrooms")] public int? Bathrooms { get; set; }
    [BsonElement("balconies")] public int? Balconies { get; set; }
    [BsonElement("floor")] public int? Floor { get; set; }
    [BsonElement("totalFloors")] public int? TotalFloors { get; set; }
    [BsonElement("lift")] public bool? Lift { get; set; }
    [BsonElement("coveredParking")] public int? CoveredParking { get; set; }
    [BsonElement("openParking")] public int? OpenParking { get; set; }
    [BsonElement("furnishing")] [BsonRepresentation(BsonType.String)] public Furnishing? Furnishing { get; set; }
    [BsonElement("ageYears")] public int? AgeYears { get; set; }
    [BsonElement("construction")] [BsonRepresentation(BsonType.String)] public ConstructionStatus? Construction { get; set; }
    [BsonElement("possessionDate")] public string? PossessionDate { get; set; }
    [BsonElement("servantRoom")] public bool ServantRoom { get; set; }
    [BsonElement("poojaRoom")] public bool PoojaRoom { get; set; }
    [BsonElement("storeRoom")] public bool StoreRoom { get; set; }
    [BsonElement("studyRoom")] public bool StudyRoom { get; set; }
    [BsonElement("powerBackup")] public bool? PowerBackup { get; set; }
    [BsonElement("gated")] public bool? Gated { get; set; }
    [BsonElement("amenities")] public List<string> Amenities { get; set; } = [];

    [BsonElement("facing")] [BsonRepresentation(BsonType.String)] public Facing? Facing { get; set; }
    [BsonElement("corner")] public bool Corner { get; set; }
    [BsonElement("parkFacing")] public bool ParkFacing { get; set; }
    [BsonElement("roadWidthFeet")] public decimal? RoadWidthFeet { get; set; }
    [BsonElement("tenure")] [BsonRepresentation(BsonType.String)] public LandTenure? Tenure { get; set; }
    [BsonElement("khasra")] public string? KhasraNumber { get; set; }

    [BsonElement("washrooms")] public int? Washrooms { get; set; }
    [BsonElement("powerLoadKw")] public decimal? PowerLoadKw { get; set; }
    [BsonElement("pantry")] public bool? Pantry { get; set; }
    [BsonElement("currentlyLeased")] public bool? CurrentlyLeased { get; set; }
    [BsonElement("leasedRent")] public decimal? LeasedRent { get; set; }

    [BsonElement("availableFrom")] public string? AvailableFrom { get; set; }

    [BsonElement("tenantPreferences")]
    [BsonRepresentation(BsonType.String)]
    public List<TenantPreference> TenantPreferences { get; set; } = [];

    [BsonElement("petsAllowed")] public bool? PetsAllowed { get; set; }

    [BsonElement("registry")] public bool? Registry { get; set; }
    [BsonElement("mutation")] public bool? Mutation { get; set; }
    [BsonElement("loanApproved")] public bool? LoanApproved { get; set; }
    [BsonElement("noc")] public bool? Noc { get; set; }
    [BsonElement("occupancyCertificate")] public bool? OccupancyCertificate { get; set; }
    [BsonElement("propertyTaxPaid")] public bool? PropertyTaxPaid { get; set; }

    [BsonElement("source")] [BsonRepresentation(BsonType.String)] public PropertySource Source { get; set; }
    [BsonElement("sourceDealerName")] public string? SourceDealerName { get; set; }
    [BsonElement("sourceDealerPhone")] public string? SourceDealerPhone { get; set; }
    [BsonElement("keysWith")] [BsonRepresentation(BsonType.String)] public KeysWith? KeysWith { get; set; }
    [BsonElement("visitTimings")] public string? VisitTimings { get; set; }
    [BsonElement("mandate")] [BsonRepresentation(BsonType.String)] public MandateKind Mandate { get; set; }
    [BsonElement("mandateUntil")] public string? MandateUntil { get; set; }
    [BsonElement("occupancy")] [BsonRepresentation(BsonType.String)] public Occupancy? Occupancy { get; set; }

    [BsonElement("photos")] public List<PhotoModel> Photos { get; set; } = [];
    [BsonElement("videoUrl")] public string? VideoUrl { get; set; }
    [BsonElement("notes")] public string? Notes { get; set; }

    /// <summary>The dealer's own fields: key → value (as text).</summary>
    [BsonElement("custom")] public Dictionary<string, string> Custom { get; set; } = [];

    [BsonElement("owners")] public List<OwnerModel> Owners { get; set; } = [];

    /// <summary>Owners' phone digits, kept for the duplicate check (design 4.6).</summary>
    [BsonElement("ownerPhones")] public List<string> OwnerPhones { get; set; } = [];

    /// <summary>Words to search in: title, locality, society, block, notes — lower-cased.</summary>
    [BsonElement("searchText")] public string SearchText { get; set; } = string.Empty;

    [BsonElement("title")] public string Title { get; set; } = string.Empty;
}
