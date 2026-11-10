using MongoDB.Bson.Serialization.Attributes;
using ZeroNovaRealEstate.Domain.Base;
using ZeroNovaRealEstate.Domain.Helper;

namespace ZeroNovaRealEstate.Domain.Documents;

/// <summary>What one agent (or everyone, for <see cref="CustomerDocument.Totals"/>) has of a customer.</summary>
public sealed class CustomerCountsModel
{
    [BsonElement("agentId")] [BsonIgnoreIfNull] public string? AgentId { get; set; }
    [BsonElement("enquiries")] public int Enquiries { get; set; }
    [BsonElement("openRequirements")] public int OpenRequirements { get; set; }
    [BsonElement("visits")] public int Visits { get; set; }
    [BsonElement("deals")] public int Deals { get; set; }
    [BsonElement("tenancies")] public int Tenancies { get; set; }

    /// <summary>Requirements closed not available or lost, enquiries closed not available.</summary>
    [BsonElement("unmet")] public int Unmet { get; set; }

    [BsonElement("lastActivityAt")] public DateTime? LastActivityAt { get; set; }
}

/// <summary>
/// The Customers list's row (C2, D32): a zeronova-lead contact with anything in Real Estate — an enquiry, a requirement, a
/// visit, a deal, a tenancy, or a party linked to the contact or a lead. A derived summary, never the customer itself (D3):
/// rebuilt from the rows on every write of one of them (<c>CustomerBook</c>), so the list is one indexed, paged read.
/// <see cref="Key"/> is the contact id, or <c>lead:{leadId}</c> while only a lead is known (merged into the contact's row
/// once an enquiry names the contact). Counts are kept per agent too, so an agent's list shows only their own share.
/// "Highlighted" is not stored: it is read at query time from the recent new matches of <see cref="RequirementIds"/>.
/// </summary>
[DocumentName("re_customers")]
public sealed class CustomerDocument : CompanyDocumentBase
{
    [BsonElement("key")] public required string Key { get; set; }
    [BsonElement("contactId")] public string? ContactId { get; set; }
    [BsonElement("leadIds")] public List<string> LeadIds { get; set; } = [];
    [BsonElement("requirementIds")] public List<string> RequirementIds { get; set; } = [];
    [BsonElement("partyIds")] public List<string> PartyIds { get; set; } = [];

    /// <summary>As the latest enquiry, requirement or visit has it, else the party's.</summary>
    [BsonElement("name")] public string? Name { get; set; }

    /// <summary>As the latest enquiry, requirement or visit has it (digits with the country code).</summary>
    [BsonElement("phone")] public string? Phone { get; set; }

    /// <summary>A party's phone, when no enquiry, requirement or visit had one — shown only with POWV.</summary>
    [BsonElement("partyPhone")] public string? PartyPhone { get; set; }

    /// <summary>The agent of the latest row.</summary>
    [BsonElement("agentId")] public string? AgentId { get; set; }

    [BsonElement("agentName")] public string? AgentName { get; set; }

    /// <summary>Every agent of any of the customer's rows (an agent's list: the customers they are agent of).</summary>
    [BsonElement("agentIds")] public List<string> AgentIds { get; set; } = [];

    [BsonElement("totals")] public CustomerCountsModel Totals { get; set; } = new();
    [BsonElement("agents")] public List<CustomerCountsModel> Agents { get; set; } = [];
    [BsonElement("lastActivityAt")] public DateTime? LastActivityAt { get; set; }

    /// <summary>Lower case: names and phones seen.</summary>
    [BsonElement("searchText")] public string SearchText { get; set; } = string.Empty;

    [BsonElement("refreshedAt")] public DateTime RefreshedAt { get; set; }
}
