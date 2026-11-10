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

    /// <summary>The zeronova-lead contact this person is (C2), when known — the customer's key.</summary>
    [BsonElement("contactId")] public string? ContactId { get; set; }

    /// <summary>Resident, NRI, OCI or foreign national (C5): s.195 TDS for a non-resident seller, FEMA for a buyer of farm land.</summary>
    [BsonElement("residency")] [BsonRepresentation(BsonType.String)] [BsonIgnoreIfNull] public PartyResidency? Residency { get; set; }

    /// <summary>For the stamp duty estimate (C5).</summary>
    [BsonElement("gender")] [BsonRepresentation(BsonType.String)] [BsonIgnoreIfNull] public PartyGender? Gender { get; set; }
}

/// <summary>What a buyer or tenant wants (design 4.4). Sizes in sq ft as well as entered; localities as keys for matching.</summary>
[DocumentName("re_requirements")]
public sealed class RequirementDocument : CompanyDocumentBase
{
    [BsonElement("leadId")] public required string LeadId { get; set; }

    /// <summary>The lead's contact in zeronova-lead (C2): the customer's key. From the app's lead picker, else learned from an enquiry.</summary>
    [BsonElement("contactId")] public string? ContactId { get; set; }

    [BsonElement("leadName")] public string? LeadName { get; set; }
    [BsonElement("leadPhone")] public string? LeadPhone { get; set; }
    [BsonElement("agentId")] public required string AgentId { get; set; }
    [BsonElement("status")] [BsonRepresentation(BsonType.String)] public RequirementStatus Status { get; set; } = RequirementStatus.Active;
    /// <summary>The free-text reason — a note beside <see cref="CloseReasonCode"/> (C3); the only reason of a row closed before C3.</summary>
    [BsonElement("closeReason")] public string? CloseReason { get; set; }

    /// <summary>Why it was closed (C3); null on a row closed before C3, or open.</summary>
    [BsonElement("closeReasonCode")] [BsonRepresentation(BsonType.String)] [BsonIgnoreIfNull]
    public RequirementCloseReason? CloseReasonCode { get; set; }

    [BsonElement("closedAt")] [BsonIgnoreIfNull] public DateTime? ClosedAt { get; set; }

    /// <summary>While <see cref="RequirementStatus.Watching"/>: when the watch ends (D33); the daily sweep then closes it.</summary>
    [BsonElement("watchUntil")] [BsonIgnoreIfNull] public DateTime? WatchUntil { get; set; }

    /// <summary>The enquiry it was made from when that enquiry was closed as not available (D33).</summary>
    [BsonElement("enquiryId")] [BsonIgnoreIfNull] public string? EnquiryId { get; set; }

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

    // ── A match to a project's unit type (D24): one per requirement and (project, unit type), pointing at the best
    // available unit of the type; it moves to the next one when that unit leaves the stock.

    /// <summary><c>{projectId}:{unitTypeId}</c>; null for a match to a single property.</summary>
    [BsonElement("groupKey")] [BsonIgnoreIfNull] public string? GroupKey { get; set; }

    [BsonElement("projectId")] [BsonIgnoreIfNull] public string? ProjectId { get; set; }
    [BsonElement("projectName")] [BsonIgnoreIfNull] public string? ProjectName { get; set; }
    [BsonElement("unitTypeId")] [BsonIgnoreIfNull] public string? UnitTypeId { get; set; }
    [BsonElement("unitTypeName")] [BsonIgnoreIfNull] public string? UnitTypeName { get; set; }

    /// <summary>How many available units of the type fit the requirement.</summary>
    [BsonElement("availableInGroup")] [BsonIgnoreIfNull] public int? AvailableInGroup { get; set; }

    /// <summary>Made while the requirement was watched (D33): the customer asked for this earlier and it is available now.</summary>
    [BsonElement("revived")] public bool Revived { get; set; }

    // ── The one-click message (D33).

    [BsonElement("messageVia")] [BsonRepresentation(BsonType.String)] [BsonIgnoreIfNull] public MatchMessageVia? MessageVia { get; set; }
    [BsonElement("messagedAt")] [BsonIgnoreIfNull] public DateTime? MessagedAt { get; set; }

    /// <summary>The send's key at zeronova-social (<c>re:{companyId}:m:{matchId}:{shareId}</c>), to find the match from its outcome.</summary>
    [BsonElement("messageKey")] [BsonIgnoreIfNull] public string? MessageKey { get; set; }

    /// <summary>zeronova-social refused or failed the send: its reason (not_connected, template_not_approved, …).</summary>
    [BsonElement("messageFailed")] [BsonIgnoreIfNull] public string? MessageFailed { get; set; }
}

/// <summary>
/// Website searches that found nothing (D34, E2), counted per India day and criteria — never who searched (no IP, no
/// browser, no person). One row per (company, day, criteria key), its count moved by <c>$inc</c>.
/// </summary>
[DocumentName("re_demand_signals")]
public sealed class DemandSignalDocument : CompanyDocumentBase
{
    /// <summary>India day, yyyy-MM-dd.</summary>
    [BsonElement("day")] public required string Day { get; set; }

    /// <summary>The criteria in one string — the row's identity within the day.</summary>
    [BsonElement("key")] public required string Key { get; set; }

    [BsonElement("purpose")] [BsonRepresentation(BsonType.String)] [BsonIgnoreIfNull] public PropertyPurpose? Purpose { get; set; }

    [BsonElement("types")]
    [BsonRepresentation(BsonType.String)]
    public List<PropertyType> Types { get; set; } = [];

    [BsonElement("city")] [BsonIgnoreIfNull] public string? City { get; set; }
    [BsonElement("locality")] [BsonIgnoreIfNull] public string? Locality { get; set; }
    [BsonElement("localityKey")] [BsonIgnoreIfNull] public string? LocalityKey { get; set; }

    /// <summary><c>50L-1Cr</c>, <c>25k-50k</c> …; null = no budget.</summary>
    [BsonElement("budgetBand")] [BsonIgnoreIfNull] public string? BudgetBand { get; set; }

    [BsonElement("bhk")] public List<int> Bhk { get; set; } = [];
    [BsonElement("count")] public int Count { get; set; }
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
    [BsonElement("gstRegistered")] public bool GstRegistered { get; set; }
    [BsonElement("gstin")] public string? Gstin { get; set; }
    [BsonElement("dealerAddress")] public string? DealerAddress { get; set; }
    [BsonElement("saleBrokeragePercent")] public decimal SaleBrokeragePercent { get; set; } = 1m;
    [BsonElement("rentBrokerageMonths")] public decimal RentBrokerageMonths { get; set; } = 1m;

    /// <summary>How long a requirement closed as not available is watched (D33).</summary>
    [BsonElement("watchDays")] public int WatchDays { get; set; } = 90;

    /// <summary>The company's approved WhatsApp template for "property now available" (D33); null = a wa.me draft.</summary>
    [BsonElement("matchTemplateName")] [BsonIgnoreIfNull] public string? MatchTemplateName { get; set; }

    [BsonElement("matchTemplateLanguage")] [BsonIgnoreIfNull] public string? MatchTemplateLanguage { get; set; }

    /// <summary>The company's state or UT (C5): properties and projects without their own take it.</summary>
    [BsonElement("defaultState")] [BsonIgnoreIfNull] public string? DefaultState { get; set; }

    /// <summary>The dealer's own PAN (C5): a business payer deducts 20% TDS on brokerage without it (or a GSTIN).</summary>
    [BsonElement("dealerPan")] [BsonIgnoreIfNull] public string? DealerPan { get; set; }

    /// <summary>The dealer's stamp duty rates (C5); null = the defaults.</summary>
    [BsonElement("stampDutyRates")] [BsonIgnoreIfNull] public List<StampDutyRateModel>? StampDutyRates { get; set; }
}

/// <summary>
/// The company's public website (cms doc 06 §12, E2): on or off, and its code — unique across companies, as Tutor's
/// coaching code is. The site's server asks for the listings by this code, with no login. One per company, made on first ask.
/// </summary>
[DocumentName("re_websites")]
public sealed class EstateWebsiteDocument : CompanyDocumentBase
{
    /// <summary>Letters and digits, upper case.</summary>
    [BsonElement("code")] public required string Code { get; set; }

    [BsonElement("enabled")] public bool Enabled { get; set; }
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

/// <summary>
/// A site visit (M2) to a property, or to a project (design 7.3: <see cref="ProjectId"/>, the unit optional). A reminder
/// bell goes to the agent once, an hour before.
/// </summary>
[DocumentName("re_visits")]
public sealed class VisitDocument : CompanyDocumentBase
{
    [BsonElement("propertyId")] public string? PropertyId { get; set; }
    [BsonElement("projectId")] public string? ProjectId { get; set; }
    [BsonElement("requirementId")] public string? RequirementId { get; set; }
    [BsonElement("leadId")] public required string LeadId { get; set; }

    /// <summary>The lead's contact (C2): from the requirement or the enquiry, else learned from an enquiry.</summary>
    [BsonElement("contactId")] public string? ContactId { get; set; }

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

/// <summary>A share link (M2): an unguessable token, what it shows (a property or a project), and how often it was opened.</summary>
[DocumentName("re_shares")]
public sealed class ShareDocument : CompanyDocumentBase
{
    [BsonElement("token")] public required string Token { get; set; }
    [BsonElement("propertyId")] public string? PropertyId { get; set; }
    [BsonElement("projectId")] public string? ProjectId { get; set; }
    [BsonElement("requirementId")] public string? RequirementId { get; set; }
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
    [BsonElement("opens")] public int Opens { get; set; }
    [BsonElement("lastOpenedAt")] public DateTime? LastOpenedAt { get; set; }

    /// <summary>Enquiries sent from the page's "Enquire" form (C1).</summary>
    [BsonElement("enquiries")] public int Enquiries { get; set; }

    [BsonElement("lastEnquiryAt")] public DateTime? LastEnquiryAt { get; set; }

    /// <summary>When the enquiries of the last hour came — the per-link guard (<c>Share:EnquiriesPerHour</c>).</summary>
    [BsonElement("recentEnquiries")] public List<DateTime> RecentEnquiries { get; set; } = [];
}

public sealed class DealPaymentModel
{
    [BsonElement("mode")] [BsonRepresentation(BsonType.String)] public Models.Deals.PaymentMode Mode { get; set; }
    [BsonElement("amount")] public decimal Amount { get; set; }
    [BsonElement("reference")] public string? Reference { get; set; }
    [BsonElement("bank")] public string? Bank { get; set; }
    [BsonElement("date")] public string? Date { get; set; }
    [BsonElement("payerId")] public string? PayerPartyId { get; set; }
}

public sealed class DealInstalmentModel
{
    [BsonElement("mode")] [BsonRepresentation(BsonType.String)] public Models.Deals.PaymentMode? Mode { get; set; }
    [BsonElement("amount")] public decimal Amount { get; set; }
    [BsonElement("dueDate")] public string? DueDate { get; set; }
}

public sealed class ChecklistItemModel
{
    [BsonElement("key")] public required string Key { get; set; }
    [BsonElement("label")] public required string Label { get; set; }
    [BsonElement("done")] public bool Done { get; set; }
    [BsonElement("date")] public string? Date { get; set; }
    [BsonElement("note")] public string? Note { get; set; }

    /// <summary>Its place in an ordered procedure (C5); null = any time.</summary>
    [BsonElement("step")] [BsonIgnoreIfNull] public int? Step { get; set; }

    /// <summary>How many days it usually takes (C5) — a hint.</summary>
    [BsonElement("typicalDays")] [BsonIgnoreIfNull] public int? TypicalDays { get; set; }

    /// <summary>What to know about it (C5): a fee, who issues it.</summary>
    [BsonElement("hint")] [BsonIgnoreIfNull] public string? Hint { get; set; }
}

public sealed class WitnessModel
{
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("address")] public string? Address { get; set; }
}

/// <summary>A deal on a property (M3): parties on both sides, money, the transfer checklist and the agreement's details.</summary>
[DocumentName("re_deals")]
public sealed class DealDocument : CompanyDocumentBase
{
    [BsonElement("code")] public required string Code { get; set; }
    [BsonElement("propertyId")] public required string PropertyId { get; set; }
    [BsonElement("kind")] [BsonRepresentation(BsonType.String)] public PropertyPurpose Kind { get; set; }
    [BsonElement("stage")] [BsonRepresentation(BsonType.String)] public Models.Deals.DealStage Stage { get; set; } = Models.Deals.DealStage.Offer;
    [BsonElement("sellerIds")] public List<string> SellerIds { get; set; } = [];
    [BsonElement("buyerIds")] public List<string> BuyerIds { get; set; } = [];
    [BsonElement("requirementId")] public string? RequirementId { get; set; }
    [BsonElement("leadId")] public string? LeadId { get; set; }
    [BsonElement("agentId")] public required string AgentId { get; set; }
    [BsonElement("price")] public decimal Price { get; set; }
    [BsonElement("earnestPercent")] public decimal? EarnestPercent { get; set; }
    [BsonElement("payments")] public List<DealPaymentModel> Payments { get; set; } = [];
    [BsonElement("schedule")] public List<DealInstalmentModel> Schedule { get; set; } = [];
    [BsonElement("finalDate")] public string? FinalDate { get; set; }
    [BsonElement("transferKind")] [BsonRepresentation(BsonType.String)] public Models.Deals.TransferKind TransferKind { get; set; }
    [BsonElement("checklist")] public List<ChecklistItemModel> Checklist { get; set; } = [];
    [BsonElement("executionPlace")] public string? ExecutionPlace { get; set; }
    [BsonElement("executionDate")] public string? ExecutionDate { get; set; }
    [BsonElement("witnesses")] public List<WitnessModel> Witnesses { get; set; } = [];
    [BsonElement("propertyDescription")] public string? PropertyDescription { get; set; }
    [BsonElement("titleNote")] public string? TitleNote { get; set; }
    [BsonElement("inclusions")] public List<string> Inclusions { get; set; } = [];
    [BsonElement("terms")] public List<string> Terms { get; set; } = [];
    [BsonElement("notes")] public string? Notes { get; set; }
    [BsonElement("completedAt")] public DateTime? CompletedAt { get; set; }
    [BsonElement("cancelReason")] public string? CancelReason { get; set; }
}

/// <summary>The dealer's own agreement template (D12): a Word file with <c>{{placeholders}}</c>, kept as bytes.</summary>
[DocumentName("re_agreement_templates")]
public sealed class AgreementTemplateDocument : CompanyDocumentBase
{
    [BsonElement("name")] public required string Name { get; set; }
    [BsonElement("kind")] [BsonRepresentation(BsonType.String)] public Models.Deals.AgreementKind Kind { get; set; }
    [BsonElement("docx")] public required byte[] Docx { get; set; }
    [BsonElement("placeholders")] public List<string> Placeholders { get; set; } = [];
}

public sealed class BrokerageReceiptModel
{
    [BsonElement("amount")] public decimal Amount { get; set; }
    [BsonElement("mode")] [BsonRepresentation(BsonType.String)] public Models.Deals.PaymentMode Mode { get; set; }
    [BsonElement("reference")] public string? Reference { get; set; }
    [BsonElement("date")] public string? Date { get; set; }
}

/// <summary>One side's brokerage on a deal (D13, M4): reckoned, taxed, invoiced, received.</summary>
[DocumentName("re_brokerages")]
public sealed class BrokerageDocument : CompanyDocumentBase
{
    [BsonElement("invoiceNo")] public required string InvoiceNo { get; set; }
    [BsonElement("dealId")] public required string DealId { get; set; }
    [BsonElement("side")] [BsonRepresentation(BsonType.String)] public Models.Money.BrokerageSide Side { get; set; }
    [BsonElement("partyId")] public string? PartyId { get; set; }
    [BsonElement("basis")] [BsonRepresentation(BsonType.String)] public Models.Money.BrokerageBasis Basis { get; set; }
    [BsonElement("rate")] public decimal Rate { get; set; }
    [BsonElement("amount")] public decimal Amount { get; set; }
    [BsonElement("gstPercent")] public decimal GstPercent { get; set; }
    [BsonElement("tdsPercent")] public decimal TdsPercent { get; set; }

    /// <summary>The TDS % was typed in on the last save (false: the rule set it, and a save without one applies the rule again).</summary>
    [BsonElement("tdsManual")] public bool TdsManual { get; set; }

    /// <summary>Who pays (C5); null on a brokerage saved before C5.</summary>
    [BsonElement("payerKind")] [BsonRepresentation(BsonType.String)] [BsonIgnoreIfNull] public Models.Money.PayerKind? PayerKind { get; set; }
    [BsonElement("receipts")] public List<BrokerageReceiptModel> Receipts { get; set; } = [];
    [BsonElement("writtenOff")] public bool WrittenOff { get; set; }
    [BsonElement("notes")] public string? Notes { get; set; }
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
}

public sealed class RentPaymentModel
{
    [BsonElement("month")] public required string Month { get; set; }
    [BsonElement("amount")] public decimal Amount { get; set; }
    [BsonElement("mode")] [BsonRepresentation(BsonType.String)] public Models.Deals.PaymentMode Mode { get; set; }
    [BsonElement("reference")] public string? Reference { get; set; }
    [BsonElement("date")] public string? Date { get; set; }
}

/// <summary>A tenancy (M4): landlords and tenants, rent and deposit, the term, the rents received, and the reminders sent.</summary>
[DocumentName("re_tenancies")]
public sealed class TenancyDocument : CompanyDocumentBase
{
    [BsonElement("propertyId")] public required string PropertyId { get; set; }
    [BsonElement("dealId")] public string? DealId { get; set; }
    [BsonElement("landlordIds")] public List<string> LandlordIds { get; set; } = [];
    [BsonElement("tenantIds")] public List<string> TenantIds { get; set; } = [];
    [BsonElement("rent")] public decimal Rent { get; set; }
    [BsonElement("deposit")] public decimal? Deposit { get; set; }
    [BsonElement("maintenance")] public decimal? Maintenance { get; set; }
    [BsonElement("startDate")] public required string StartDate { get; set; }
    [BsonElement("months")] public int Months { get; set; }
    [BsonElement("endDate")] public required string EndDate { get; set; }
    [BsonElement("rentDueDay")] public int RentDueDay { get; set; }
    [BsonElement("escalation")] public decimal? EscalationPercent { get; set; }
    [BsonElement("lockIn")] public int? LockInMonths { get; set; }
    [BsonElement("notice")] public int? NoticeMonths { get; set; }
    [BsonElement("status")] [BsonRepresentation(BsonType.String)] public Models.Money.TenancyStatus Status { get; set; } = Models.Money.TenancyStatus.Active;
    [BsonElement("payments")] public List<RentPaymentModel> Payments { get; set; } = [];
    [BsonElement("agentId")] public required string AgentId { get; set; }
    [BsonElement("notes")] public string? Notes { get; set; }

    /// <summary>The months (yyyy-MM) a rent-due bell went for, and whether the renewal bell went.</summary>
    [BsonElement("remindedMonths")] public List<string> RemindedMonths { get; set; } = [];
    [BsonElement("renewalReminded")] public bool RenewalReminded { get; set; }

    // ── C5: the agreement date, the lease terms the lease templates need, and the paperwork after signing (D38).

    [BsonElement("agreementDate")] [BsonIgnoreIfNull] public string? AgreementDate { get; set; }
    [BsonElement("rentFreeDays")] [BsonIgnoreIfNull] public int? RentFreeDays { get; set; }
    [BsonElement("permittedUse")] [BsonIgnoreIfNull] public string? PermittedUse { get; set; }

    [BsonElement("policeVerified")] public bool PoliceVerified { get; set; }
    [BsonElement("policeVerifiedOn")] [BsonIgnoreIfNull] public string? PoliceVerifiedOn { get; set; }
    [BsonElement("policeStation")] [BsonIgnoreIfNull] public string? PoliceStation { get; set; }
    [BsonElement("policeReference")] [BsonIgnoreIfNull] public string? PoliceReference { get; set; }
    [BsonElement("policeDocument")] [BsonIgnoreIfNull] public PhotoModel? PoliceDocument { get; set; }
    [BsonElement("policeReminded")] public bool PoliceReminded { get; set; }

    /// <summary>Chandigarh only: when the Rent Authority was told (due 2 months after the agreement), and the UIN.</summary>
    [BsonElement("rentAuthorityDoneOn")] [BsonIgnoreIfNull] public string? RentAuthorityDoneOn { get; set; }

    [BsonElement("rentAuthorityUin")] [BsonIgnoreIfNull] public string? RentAuthorityUin { get; set; }
    [BsonElement("rentAuthorityReminded")] public bool RentAuthorityReminded { get; set; }

    /// <summary>
    /// The rent bells found the property is not in Chandigarh when the intimation's window opened: not read for it again
    /// (cleared on the tenancy's next save).
    /// </summary>
    [BsonElement("rentAuthorityNotApplicable")] public bool RentAuthorityNotApplicable { get; set; }
}

/// <summary>One state's stamp duty rates (C5), in % — see <see cref="Models.Settings.StampDutyRateDto"/>.</summary>
public sealed class StampDutyRateModel
{
    [BsonElement("state")] public required string State { get; set; }
    [BsonElement("area")] [BsonRepresentation(BsonType.String)] [BsonIgnoreIfNull] public StampDutyArea? Area { get; set; }
    [BsonElement("men")] [BsonIgnoreIfNull] public decimal? MenPercent { get; set; }
    [BsonElement("women")] [BsonIgnoreIfNull] public decimal? WomenPercent { get; set; }
    [BsonElement("joint")] [BsonIgnoreIfNull] public decimal? JointPercent { get; set; }
    [BsonElement("registration")] [BsonIgnoreIfNull] public decimal? RegistrationPercent { get; set; }
    [BsonElement("note")] [BsonIgnoreIfNull] public string? Note { get; set; }
}
