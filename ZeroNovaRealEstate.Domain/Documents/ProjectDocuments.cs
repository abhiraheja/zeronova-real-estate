using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ZeroNovaRealEstate.Domain.Base;
using ZeroNovaRealEstate.Domain.Helper;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Domain.Documents;

public sealed class MediaFileModel
{
    [BsonElement("key")] public required string Key { get; set; }
    [BsonElement("url")] public required string Url { get; set; }
    [BsonElement("name")] public string? Name { get; set; }
}

public sealed class CpUnitTypeAmountModel
{
    [BsonElement("unitTypeId")] public required string UnitTypeId { get; set; }
    [BsonElement("amount")] public decimal Amount { get; set; }
}

/// <summary>A partner project's channel-partner terms (D17). Seen with PBRV, changed with PBRM.</summary>
public sealed class CpTermsModel
{
    [BsonElement("commissionPercent")] public decimal? CommissionPercent { get; set; }
    [BsonElement("perUnitType")] public List<CpUnitTypeAmountModel> PerUnitType { get; set; } = [];
    [BsonElement("paidWhen")] public string? PaidWhen { get; set; }
    [BsonElement("leadValidityDays")] public int? LeadValidityDays { get; set; }
    [BsonElement("validUntil")] public string? ValidUntil { get; set; }
    [BsonElement("notes")] public string? Notes { get; set; }
}

public sealed class ReraModel
{
    [BsonElement("number")] public required string Number { get; set; }
    [BsonElement("state")] public string? State { get; set; }
    [BsonElement("registeredOn")] public string? RegisteredOn { get; set; }
    [BsonElement("validUntil")] public string? ValidUntil { get; set; }
    [BsonElement("url")] public string? Url { get; set; }
}

public sealed class ApprovalModel
{
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("number")] public string? Number { get; set; }
    [BsonElement("date")] public string? Date { get; set; }
}

public sealed class LandmarkModel
{
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("km")] public decimal? DistanceKm { get; set; }
    [BsonElement("minutes")] public int? Minutes { get; set; }
}

public sealed class TowerModel
{
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("floors")] public int? Floors { get; set; }
}

public sealed class KeyFigureModel
{
    [BsonElement("label")] public required string Label { get; set; }
    [BsonElement("value")] public required string Value { get; set; }
}

public sealed class SpecificationModel
{
    [BsonElement("section")] public required string Section { get; set; }
    [BsonElement("text")] public required string Text { get; set; }
}

public sealed class MilestoneModel
{
    [BsonElement("id")] public required string Id { get; set; }
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("status")] [BsonRepresentation(BsonType.String)] public MilestoneStatus Status { get; set; }
    [BsonElement("date")] public string? Date { get; set; }
}

public sealed class ConstructionUpdateModel
{
    [BsonElement("id")] public required string Id { get; set; }
    [BsonElement("date")] public required string Date { get; set; }
    [BsonElement("text")] public required string Text { get; set; }
    [BsonElement("photos")] public List<PhotoModel> Photos { get; set; } = [];
}

/// <summary>
/// A project (design 7, D17): a builder's own, or another builder's sold as a channel partner. Towers, milestones and
/// construction updates live inside; unit types, price lists and payment plans are their own collections; units are
/// properties with <c>projectId</c> (D18). A partner project is confirmed as a whole (<see cref="LastConfirmedAt"/>, D21).
/// </summary>
[DocumentName("re_projects")]
public sealed class ProjectDocument : CompanyDocumentBase
{
    /// <summary><c>PJ-0001</c>, numbered per company.</summary>
    [BsonElement("code")] public required string Code { get; set; }

    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("kind")] [BsonRepresentation(BsonType.String)] public ProjectKind Kind { get; set; }

    [BsonElement("builderName")] public string? BuilderName { get; set; }
    [BsonElement("builderContactName")] public string? BuilderContactName { get; set; }
    [BsonElement("builderPhone")] public string? BuilderPhone { get; set; }
    [BsonElement("builderEmail")] public string? BuilderEmail { get; set; }
    [BsonElement("cpTerms")] public CpTermsModel? CpTerms { get; set; }

    [BsonElement("categories")]
    [BsonRepresentation(BsonType.String)]
    public List<ProjectCategory> Categories { get; set; } = [];

    [BsonElement("stage")] [BsonRepresentation(BsonType.String)] public ProjectStage Stage { get; set; }
    [BsonElement("launchDate")] public string? LaunchDate { get; set; }
    [BsonElement("possessionDate")] public string? PossessionDate { get; set; }

    [BsonElement("reras")] public List<ReraModel> Reras { get; set; } = [];

    /// <summary>Needs no RERA registration (with the reason) — so an own project may be shared without one.</summary>
    [BsonElement("reraNotRequired")] public bool ReraNotRequired { get; set; }

    [BsonElement("reraExemptReason")] public string? ReraExemptReason { get; set; }
    [BsonElement("approvals")] public List<ApprovalModel> Approvals { get; set; } = [];

    /// <summary>yyyy-MM-dd the occupancy certificate came: unsold units carry no GST from then.</summary>
    [BsonElement("ocReceivedOn")] public string? OcReceivedOn { get; set; }

    [BsonElement("city")] public string? City { get; set; }
    [BsonElement("locality")] public required string Locality { get; set; }
    [BsonElement("localityKey")] public required string LocalityKey { get; set; }
    [BsonElement("address")] public string? Address { get; set; }
    [BsonElement("lat")] public double? Latitude { get; set; }
    [BsonElement("lng")] public double? Longitude { get; set; }
    [BsonElement("landmarks")] public List<LandmarkModel> Landmarks { get; set; } = [];

    [BsonElement("landArea")] public AreaModel? LandArea { get; set; }
    [BsonElement("towers")] public List<TowerModel> Towers { get; set; } = [];
    [BsonElement("keyFigures")] public List<KeyFigureModel> KeyFigures { get; set; } = [];

    [BsonElement("overview")] public string? Overview { get; set; }
    [BsonElement("highlights")] public List<string> Highlights { get; set; } = [];
    [BsonElement("amenities")] public List<string> Amenities { get; set; } = [];
    [BsonElement("specifications")] public List<SpecificationModel> Specifications { get; set; } = [];

    [BsonElement("photos")] public List<PhotoModel> Photos { get; set; } = [];
    [BsonElement("videoUrl")] public string? VideoUrl { get; set; }
    [BsonElement("droneUrl")] public string? DroneUrl { get; set; }
    [BsonElement("brochure")] public MediaFileModel? Brochure { get; set; }
    [BsonElement("sitePlan")] public PhotoModel? SitePlan { get; set; }

    [BsonElement("milestones")] public List<MilestoneModel> Milestones { get; set; } = [];
    [BsonElement("updates")] public List<ConstructionUpdateModel> Updates { get; set; } = [];

    [BsonElement("gstPercent")] public decimal? GstPercent { get; set; }
    [BsonElement("notes")] public string? Notes { get; set; }

    [BsonElement("lastConfirmedAt")] public DateTime LastConfirmedAt { get; set; }

    /// <summary>Name, code, locality, city, builder — lower-cased.</summary>
    [BsonElement("searchText")] public string SearchText { get; set; } = string.Empty;
}

/// <summary>A kind of unit in a project (design 7.2): its units take these fields and keep them in step (D18).</summary>
[DocumentName("re_unit_types")]
public sealed class UnitTypeDocument : CompanyDocumentBase
{
    [BsonElement("projectId")] public required string ProjectId { get; set; }
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("type")] [BsonRepresentation(BsonType.String)] public PropertyType Type { get; set; }
    [BsonElement("bhk")] public int? Bhk { get; set; }
    [BsonElement("bathrooms")] public int? Bathrooms { get; set; }
    [BsonElement("balconies")] public int? Balconies { get; set; }
    [BsonElement("carpetArea")] public AreaModel? CarpetArea { get; set; }
    [BsonElement("builtUpArea")] public AreaModel? BuiltUpArea { get; set; }
    [BsonElement("superArea")] public AreaModel? SuperArea { get; set; }
    [BsonElement("plotArea")] public AreaModel? PlotArea { get; set; }
    [BsonElement("frontFeet")] public decimal? FrontFeet { get; set; }
    [BsonElement("depthFeet")] public decimal? DepthFeet { get; set; }
    [BsonElement("floorPlan")] public PhotoModel? FloorPlan { get; set; }
    [BsonElement("priceOnRequest")] public bool PriceOnRequest { get; set; }

    /// <summary>This type's GST %, over the project's.</summary>
    [BsonElement("gstPercent")] public decimal? GstPercent { get; set; }

    [BsonElement("notes")] public string? Notes { get; set; }
    [BsonElement("order")] public int Order { get; set; }
}

public sealed class UnitTypeRateModel
{
    [BsonElement("unitTypeId")] public required string UnitTypeId { get; set; }
    [BsonElement("basis")] [BsonRepresentation(BsonType.String)] public RateBasis Basis { get; set; }
    [BsonElement("rate")] public decimal Rate { get; set; }
    [BsonElement("area")] [BsonRepresentation(BsonType.String)] public AreaBasis Area { get; set; }
}

public sealed class ChargeModel
{
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("kind")] [BsonRepresentation(BsonType.String)] public ChargeKind Kind { get; set; }
    [BsonElement("amount")] public decimal Amount { get; set; }
    [BsonElement("mandatory")] public bool Mandatory { get; set; }
    [BsonElement("applies")] [BsonRepresentation(BsonType.String)] public ChargeApplies Applies { get; set; }
    [BsonElement("floorFrom")] public int? FloorFrom { get; set; }
    [BsonElement("floorTo")] public int? FloorTo { get; set; }
    [BsonElement("aboveFloor")] public int? AboveFloor { get; set; }
    [BsonElement("taxable")] public bool Taxable { get; set; }
    [BsonElement("unitTypeIds")] public List<string> UnitTypeIds { get; set; } = [];
}

/// <summary>One version of a project's price list (D22). Versions are never changed or removed; the newest prices the units.</summary>
[DocumentName("re_price_lists")]
public sealed class PriceListDocument : CompanyDocumentBase
{
    [BsonElement("projectId")] public required string ProjectId { get; set; }
    /// <summary>1, 2, 3 … — the price list's own version (the document's <c>Version</c> is its concurrency stamp).</summary>
    [BsonElement("listVersion")] public int ListVersion { get; set; }
    [BsonElement("effectiveFrom")] public required string EffectiveFrom { get; set; }
    [BsonElement("rates")] public List<UnitTypeRateModel> Rates { get; set; } = [];
    [BsonElement("charges")] public List<ChargeModel> Charges { get; set; } = [];
    [BsonElement("notes")] public string? Notes { get; set; }
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
}

public sealed class PlanStepModel
{
    [BsonElement("label")] public required string Label { get; set; }
    [BsonElement("percent")] public decimal? Percent { get; set; }
    [BsonElement("amount")] public decimal? Amount { get; set; }
    [BsonElement("due")] [BsonRepresentation(BsonType.String)] public StepDue Due { get; set; }
    [BsonElement("days")] public int? Days { get; set; }
    [BsonElement("milestoneId")] public string? MilestoneId { get; set; }
}

/// <summary>A payment plan of a project (D23): steps with a percent or an amount and when each falls due.</summary>
[DocumentName("re_payment_plans")]
public sealed class PaymentPlanDocument : CompanyDocumentBase
{
    [BsonElement("projectId")] public required string ProjectId { get; set; }
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("kind")] [BsonRepresentation(BsonType.String)] public PaymentPlanKind Kind { get; set; }
    [BsonElement("steps")] public List<PlanStepModel> Steps { get; set; } = [];
    [BsonElement("notes")] public string? Notes { get; set; }
}
