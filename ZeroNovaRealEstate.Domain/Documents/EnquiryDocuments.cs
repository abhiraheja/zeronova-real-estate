using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ZeroNovaRealEstate.Domain.Base;
using ZeroNovaRealEstate.Domain.Helper;
using ZeroNovaRealEstate.Models.Enquiries;

namespace ZeroNovaRealEstate.Domain.Documents;

/// <summary>A form's extra field, kept as a pair (a key may hold any character).</summary>
public sealed class EnquiryFieldModel
{
    [BsonElement("key")] public required string Key { get; set; }
    [BsonElement("value")] public required string Value { get; set; }
}

/// <summary>
/// A Real Estate enquiry (D27): one person asking about a property, a project (and unit type) or a general need, as
/// zeronova-lead announced it (<c>lead-enquiry-received</c>, D28) — so the lead and the enquiry always agree. Unique per
/// company by <see cref="Ref"/> (the capture's external ref, else the lead's timeline row). The titles are the ones of the
/// day it came.
/// </summary>
[DocumentName("re_enquiries")]
public sealed class EnquiryDocument : CompanyDocumentBase
{
    /// <summary><c>E-0001</c>, numbered per company.</summary>
    [BsonElement("code")] public required string Code { get; set; }

    /// <summary>The dedup key: the capture's external ref (CMS enquiry id, our <c>estate:…</c> ref), else the lead's activity id.</summary>
    [BsonElement("ref")] public required string Ref { get; set; }

    [BsonElement("occurredAt")] public DateTime OccurredAt { get; set; }
    [BsonElement("source")] [BsonRepresentation(BsonType.String)] public EnquirySource Source { get; set; }
    [BsonElement("form")] public string? Form { get; set; }

    /// <summary>zeronova-lead's lower-case channel (website, whatsapp, call, manual …).</summary>
    [BsonElement("channel")] public string? Channel { get; set; }

    [BsonElement("leadId")] public string? LeadId { get; set; }
    [BsonElement("contactId")] public string? ContactId { get; set; }
    [BsonElement("isNewLead")] public bool IsNewLead { get; set; }
    [BsonElement("activityId")] public string? ActivityId { get; set; }
    [BsonElement("name")] public string? Name { get; set; }

    /// <summary>Digits with the country code.</summary>
    [BsonElement("phone")] public string? Phone { get; set; }

    [BsonElement("email")] public string? Email { get; set; }

    [BsonElement("propertyId")] public string? PropertyId { get; set; }
    [BsonElement("propertyCode")] public string? PropertyCode { get; set; }
    [BsonElement("propertyTitle")] public string? PropertyTitle { get; set; }
    [BsonElement("projectId")] public string? ProjectId { get; set; }
    [BsonElement("projectName")] public string? ProjectName { get; set; }
    [BsonElement("unitTypeId")] public string? UnitTypeId { get; set; }
    [BsonElement("unitTypeName")] public string? UnitTypeName { get; set; }

    [BsonElement("message")] public string? Message { get; set; }
    [BsonElement("extra")] public List<EnquiryFieldModel> Extra { get; set; } = [];
    [BsonElement("sourceUrl")] public string? SourceUrl { get; set; }
    [BsonElement("shareToken")] public string? ShareToken { get; set; }

    /// <summary>The lead's owner when it came — or the agent our own capture named.</summary>
    [BsonElement("agentId")] public string? AgentId { get; set; }

    [BsonElement("agentName")] public string? AgentName { get; set; }

    [BsonElement("status")] [BsonRepresentation(BsonType.String)] public EnquiryStatus Status { get; set; } = EnquiryStatus.New;
    [BsonElement("closeReason")] [BsonRepresentation(BsonType.String)] public EnquiryCloseReason? CloseReason { get; set; }

    /// <summary>What was asked for and not available (close reason NotAvailable) — C3 turns it into a watched requirement.</summary>
    [BsonElement("wanted")] public string? Wanted { get; set; }

    [BsonElement("closeNote")] public string? CloseNote { get; set; }
    [BsonElement("requirementId")] public string? RequirementId { get; set; }
    [BsonElement("visitId")] public string? VisitId { get; set; }
    [BsonElement("contactedAt")] public DateTime? ContactedAt { get; set; }
    [BsonElement("closedAt")] public DateTime? ClosedAt { get; set; }

    /// <summary>Lower case: name, phone, email, code, property / project / unit type — for the screen's search.</summary>
    [BsonElement("searchText")] public string SearchText { get; set; } = string.Empty;

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; }
}
