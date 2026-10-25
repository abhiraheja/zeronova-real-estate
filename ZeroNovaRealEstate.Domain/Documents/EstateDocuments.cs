using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ZeroNovaRealEstate.Domain.Base;
using ZeroNovaRealEstate.Domain.Helper;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Settings;

namespace ZeroNovaRealEstate.Domain.Documents;

/// <summary>
/// A person in the dealer's papers (design 4.3): an owner, later perhaps a buyer. Aadhaar and PAN are sealed with the
/// shared master key (D11); only their last four characters are kept in clear, for display.
/// </summary>
[DocumentName("re_parties")]
public sealed class PartyDocument : CompanyDocumentBase
{
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("relation")] [BsonRepresentation(BsonType.String)] public RelationKind? Relation { get; set; }
    [BsonElement("relativeName")] public string? RelativeName { get; set; }

    /// <summary>Digits with the country code.</summary>
    [BsonElement("phone")] public string? Phone { get; set; }

    [BsonElement("whatsApp")] public string? WhatsApp { get; set; }
    [BsonElement("email")] public string? Email { get; set; }
    [BsonElement("address")] public string? Address { get; set; }
    [BsonElement("aadhaarSealed")] public string? AadhaarSealed { get; set; }
    [BsonElement("aadhaarLast4")] public string? AadhaarLast4 { get; set; }
    [BsonElement("panSealed")] public string? PanSealed { get; set; }
    [BsonElement("panLast4")] public string? PanLast4 { get; set; }
    [BsonElement("leadId")] public string? LeadId { get; set; }
}

/// <summary>What a buyer or tenant wants (design 4.4). Sizes in sq ft as well as entered; localities as keys for matching.</summary>
[DocumentName("re_requirements")]
public sealed class RequirementDocument : CompanyDocumentBase
{
    [BsonElement("leadId")] public required string LeadId { get; set; }
    [BsonElement("leadName")] public string? LeadName { get; set; }
    [BsonElement("leadPhone")] public string? LeadPhone { get; set; }
    [BsonElement("agentId")] public required string AgentId { get; set; }
    [BsonElement("status")] [BsonRepresentation(BsonType.String)] public RequirementStatus Status { get; set; } = RequirementStatus.Active;
    [BsonElement("closeReason")] public string? CloseReason { get; set; }

    [BsonElement("purpose")] [BsonRepresentation(BsonType.String)] public PropertyPurpose Purpose { get; set; }

    [BsonElement("types")]
    [BsonRepresentation(BsonType.String)]
    public List<PropertyType> Types { get; set; } = [];

    [BsonElement("city")] public string? City { get; set; }
    [BsonElement("localities")] public List<string> Localities { get; set; } = [];
    [BsonElement("localityKeys")] public List<string> LocalityKeys { get; set; } = [];
    [BsonElement("budgetMin")] public decimal? BudgetMin { get; set; }
    [BsonElement("budgetMax")] public decimal? BudgetMax { get; set; }
    [BsonElement("sizeMin")] public AreaModel? SizeMin { get; set; }
    [BsonElement("sizeMax")] public AreaModel? SizeMax { get; set; }
    [BsonElement("bhk")] public List<int> Bhk { get; set; } = [];

    [BsonElement("facings")]
    [BsonRepresentation(BsonType.String)]
    public List<Facing> Facings { get; set; } = [];

    [BsonElement("cornerOnly")] public bool CornerOnly { get; set; }
    [BsonElement("parkFacingOnly")] public bool ParkFacingOnly { get; set; }
    [BsonElement("floor")] [BsonRepresentation(BsonType.String)] public FloorPreference Floor { get; set; }
    [BsonElement("needsLift")] public bool NeedsLift { get; set; }
    [BsonElement("needsParking")] public bool NeedsParking { get; set; }

    [BsonElement("furnishings")]
    [BsonRepresentation(BsonType.String)]
    public List<Furnishing> Furnishings { get; set; } = [];

    [BsonElement("tenantType")] [BsonRepresentation(BsonType.String)] public TenantPreference? TenantType { get; set; }
    [BsonElement("neededBy")] public string? NeededBy { get; set; }
    [BsonElement("notes")] public string? Notes { get; set; }
}

/// <summary>
/// A property that fits a requirement (D10), unique per pair. A new match that no longer fits is dropped; one shown,
/// interested or rejected stays as history and is never offered again.
/// </summary>
[DocumentName("re_matches")]
public sealed class MatchDocument : CompanyDocumentBase
{
    [BsonElement("propertyId")] public required string PropertyId { get; set; }
    [BsonElement("requirementId")] public required string RequirementId { get; set; }
    [BsonElement("agentId")] public required string AgentId { get; set; }
    [BsonElement("score")] public int Score { get; set; }
    [BsonElement("reasons")] public List<string> Reasons { get; set; } = [];
    [BsonElement("status")] [BsonRepresentation(BsonType.String)] public MatchStatus Status { get; set; } = MatchStatus.New;
    [BsonElement("rejectReason")] public string? RejectReason { get; set; }
    [BsonElement("priceWhenMatched")] public decimal? PriceWhenMatched { get; set; }
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; }
}

/// <summary>The dealer's settings (design 4.2). One per company; missing = the defaults.</summary>
[DocumentName("re_settings")]
public sealed class EstateSettingsDocument : CompanyDocumentBase
{
    [BsonElement("marlaSqFt")] public decimal MarlaSqFt { get; set; } = 272.25m;
    [BsonElement("kanalSqFt")] public decimal KanalSqFt { get; set; } = 5445m;
    [BsonElement("bighaSqFt")] public decimal BighaSqFt { get; set; } = 27225m;
    [BsonElement("biswaSqFt")] public decimal BiswaSqFt { get; set; } = 1361.25m;
    [BsonElement("kathaSqFt")] public decimal KathaSqFt { get; set; } = 1361.25m;
    [BsonElement("budgetTolerance")] public int BudgetTolerancePercent { get; set; } = 10;
    [BsonElement("saleFreshDays")] public int SaleFreshDays { get; set; } = 30;
    [BsonElement("rentFreshDays")] public int RentFreshDays { get; set; } = 15;
    [BsonElement("matchThreshold")] public int MatchThreshold { get; set; } = 50;
    [BsonElement("defaultCity")] public string? DefaultCity { get; set; }
    [BsonElement("reraNumber")] public string? ReraNumber { get; set; }
    [BsonElement("dealerName")] public string? DealerName { get; set; }
    [BsonElement("dealerPhone")] public string? DealerPhone { get; set; }
}

/// <summary>A field the dealer added (D4).</summary>
[DocumentName("re_custom_fields")]
public sealed class CustomFieldDocument : CompanyDocumentBase
{
    [BsonElement("key")] public required string Key { get; set; }
    [BsonElement("label")] public required string Label { get; set; }
    [BsonElement("kind")] [BsonRepresentation(BsonType.String)] public CustomFieldKind Kind { get; set; }
    [BsonElement("options")] public List<string> Options { get; set; } = [];

    [BsonElement("propertyTypes")]
    [BsonRepresentation(BsonType.String)]
    public List<PropertyType> PropertyTypes { get; set; } = [];

    [BsonElement("filterable")] public bool Filterable { get; set; }
    [BsonElement("order")] public int Order { get; set; }
}

/// <summary>A sector, colony or society the dealer has used — made the first time it is typed.</summary>
[DocumentName("re_localities")]
public sealed class LocalityDocument : CompanyDocumentBase
{
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("key")] public required string Key { get; set; }
    [BsonElement("city")] public string? City { get; set; }
}

/// <summary>A per-company counter (property codes), moved by an atomic <c>$inc</c>.</summary>
[DocumentName("re_counters")]
public sealed class CounterDocument : CompanyDocumentBase
{
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("value")] public long Value { get; set; }
}

/// <summary>A site visit (M2). A reminder bell goes to the agent once, an hour before.</summary>
[DocumentName("re_visits")]
public sealed class VisitDocument : CompanyDocumentBase
{
    [BsonElement("propertyId")] public required string PropertyId { get; set; }
    [BsonElement("requirementId")] public string? RequirementId { get; set; }
    [BsonElement("leadId")] public required string LeadId { get; set; }
    [BsonElement("leadName")] public string? LeadName { get; set; }
    [BsonElement("leadPhone")] public string? LeadPhone { get; set; }
    [BsonElement("agentId")] public required string AgentId { get; set; }
    [BsonElement("at")] public DateTime At { get; set; }
    [BsonElement("meetingPoint")] public string? MeetingPoint { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public Models.Visits.VisitStatus Status { get; set; } = Models.Visits.VisitStatus.Scheduled;

    [BsonElement("outcome")]
    [BsonRepresentation(BsonType.String)]
    public Models.Visits.VisitOutcome? Outcome { get; set; }

    [BsonElement("feedback")] public string? Feedback { get; set; }
    [BsonElement("notes")] public string? Notes { get; set; }
    [BsonElement("reminded")] public bool Reminded { get; set; }
}

/// <summary>A share link (M2): an unguessable token, what it shows, and how often it was opened.</summary>
[DocumentName("re_shares")]
public sealed class ShareDocument : CompanyDocumentBase
{
    [BsonElement("token")] public required string Token { get; set; }
    [BsonElement("propertyId")] public required string PropertyId { get; set; }
    [BsonElement("requirementId")] public string? RequirementId { get; set; }
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
    [BsonElement("opens")] public int Opens { get; set; }
    [BsonElement("lastOpenedAt")] public DateTime? LastOpenedAt { get; set; }
}
