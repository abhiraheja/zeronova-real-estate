using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNova_Packages.Messaging.Abstractions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Events;

namespace ZeroNovaRealEstate.Application.Features.Enquiries;

/// <summary>
/// zeronova-lead's <c>lead-enquiry-received</c> (<c>LeadEnquiryReceivedPayload</c>, D28): one captured enquiry — a website
/// form, a share page, "Add enquiry" — on a new lead or a repeat on an open one. Field names are zeronova-lead's. Its
/// lead <c>Source</c> is not read: the envelope owns that name (<see cref="ConsumerNotification.Source"/>); the channels
/// say the same. Enums (status, classification, activity channel) are read as their names.
/// </summary>
public sealed class LeadEnquiryReceivedNotification : ConsumerNotification
{
    public string CompanyId { get; set; } = string.Empty;
    public string? LeadId { get; set; }
    public string? ContactId { get; set; }
    public string? NewStatus { get; set; }
    public string? NewClassification { get; set; }
    public string? Name { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>E.164 (<c>+919876543210</c>).</summary>
    public string? Phone { get; set; }

    public string? Email { get; set; }
    public List<string>? Tags { get; set; }
    public string? NewAssignedTo { get; set; }
    public string? NewAssignedToName { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Lower case: website, whatsapp, call, manual, …</summary>
    public string? Channel { get; set; }

    public string? Form { get; set; }
    public string? Product { get; set; }
    public string? ReceivedOn { get; set; }
    public bool IsNewLead { get; set; }
    public string? ActivityId { get; set; }

    /// <summary>The timeline row's channel by name: Website, WhatsApp, Call, Email, Instagram, Internal, …</summary>
    public string? ActivityChannel { get; set; }

    public string? ExternalRef { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public string? SourceUrl { get; set; }
    public string? MessagePreview { get; set; }
    public string? AssignedTo { get; set; }
    public string? AssignedToName { get; set; }
}

/// <summary>
/// Keeps the Real Estate enquiries of <c>lead-enquiry-received</c> in <c>re_enquiries</c> (D28), once per ref. Which
/// enquiries are Real Estate's (conservative — anything else is left to its own product):
/// <list type="number">
/// <item>the metadata's <c>product</c> is <c>REAL_ESTATE</c>;</item>
/// <item>the metadata carries <c>estate = "1"</c> (this service's own captures: share page, "Add enquiry");</item>
/// <item>the metadata carries a <c>propertyId</c>, <c>projectId</c> or <c>unitTypeId</c>;</item>
/// <item>the form is one only the Real Estate website has (<see cref="EstateWebsiteForms"/>) and the company's Real
/// Estate website is on. The generic contact and call-back forms count only through 1 or 3 (zeronova-cms is to send
/// <c>product: REAL_ESTATE</c>, D29).</item>
/// </list>
/// The ref is the capture's external ref, else the lead's timeline row id; a redelivery or a parallel delivery adds
/// nothing. The listing is found by id, slug or code (the website may send any), its titles kept as of today.
/// </summary>
public sealed class LeadEnquiryReceivedHandler(
    IEnquiryRepository enquiries, ICounterRepository counters, IPropertyRepository properties, IProjectRepository projects, IUnitTypeRepository unitTypes,
    IRequirementRepository requirements, IWebsiteRepository websites, TimeProvider clock, ILogger<LeadEnquiryReceivedHandler> logger,
    Customers.CustomerBook customers)
    : INotificationHandler<LeadEnquiryReceivedNotification>
{
    public const string CodeCounter = "enquiry";

    /// <summary>The Real Estate website's own forms (zeronova-cms EstateForms) — never another template's.</summary>
    public static readonly IReadOnlyCollection<string> EstateWebsiteForms = ["site-visit", "post-property", "requirement", "project-enquiry"];

    /// <summary>Metadata keys that are not the form's extra fields.</summary>
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        EnquiryMetadata.Product, EnquiryMetadata.Estate, EnquiryMetadata.Form, EnquiryMetadata.PropertyId, EnquiryMetadata.ProjectId,
        EnquiryMetadata.UnitTypeId, EnquiryMetadata.ShareToken, EnquiryMetadata.RequirementId, EnquiryMetadata.Source, EnquiryMetadata.AgentId,
        EnquiryMetadata.AgentName, EnquiryMetadata.Site, "course",
    };

    public async Task Handle(LeadEnquiryReceivedNotification n, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(n.CompanyId))
            return;
        var company = n.CompanyId.Trim();
        var meta = Trusted(Metadata(n.Metadata), n.ExternalRef);
        if (!await IsRealEstateAsync(n, meta, company, ct))
            return;

        // C2: the enquiry names the lead's contact — the lead's rows without one take it, and the customer's summary follows.
        var lead = EstateText.Clean(n.LeadId);
        var contact = EstateText.Clean(n.ContactId);
        if (lead is not null && contact is not null)
            await customers.BackfillAsync(lead, contact, ct, company);
        await KeepAsync(n, meta, company, ct);
        await customers.TouchAsync(contact, [lead], ct, company);
    }

    private async Task KeepAsync(LeadEnquiryReceivedNotification n, Dictionary<string, string> meta, string company, CancellationToken ct)
    {
        var reference = EstateText.Clean(n.ExternalRef) ?? EstateText.Clean(n.ActivityId) ?? n.MessageId;
        if (await enquiries.GetDocuments(e => e.Ref == reference, true, company).FirstOrDefaultAsync(ct) is { } kept)
        {
            // Already kept (a redelivery, or the same capture sent again). One whose number was never given (its write's
            // numbering failed) gets it now — only when it is a minute old, so a parallel delivery never numbers it twice.
            if (kept.Code.StartsWith(PendingCode, StringComparison.Ordinal) && kept.CreatedAt < clock.GetUtcNow().UtcDateTime.AddMinutes(-1))
                await NumberAsync(kept, company, ct);
            return;
        }

        var listing = await EnquiryListings.ResolveAsync(company, meta.GetValueOrDefault(EnquiryMetadata.PropertyId), meta.GetValueOrDefault(EnquiryMetadata.ProjectId),
            meta.GetValueOrDefault(EnquiryMetadata.UnitTypeId), properties, projects, unitTypes, ct);
        var product = meta.GetValueOrDefault(EnquiryMetadata.Product);
        // Trusted() left the agent keys only on this service's own captures (never on the AI office's).
        var ownCapture = meta.GetValueOrDefault(EnquiryMetadata.Estate) == "1";

        string? agentId;
        string? agentName;
        if (ownCapture && EstateText.Clean(meta.GetValueOrDefault(EnquiryMetadata.AgentId)) is { } named)
        {
            agentId = named;
            agentName = EstateText.Clean(meta.GetValueOrDefault(EnquiryMetadata.AgentName))
                ?? (named == (n.AssignedTo ?? n.NewAssignedTo) ? EstateText.Clean(n.AssignedToName ?? n.NewAssignedToName) : null);
        }
        else
        {
            agentId = EstateText.Clean(n.AssignedTo ?? n.NewAssignedTo);
            agentName = EstateText.Clean(n.AssignedToName ?? n.NewAssignedToName);
        }

        // A share made for a requirement: linked when the enquiry reached that requirement's customer.
        string? requirementId = null;
        if (EstateText.Clean(meta.GetValueOrDefault(EnquiryMetadata.RequirementId)) is { } rid && ObjectId.TryParse(rid, out _) && n.LeadId is { } leadId
            && await requirements.GetDocuments(r => r.Id == rid && r.LeadId == leadId, false, company).AnyAsync(ct))
            requirementId = rid;

        var now = clock.GetUtcNow().UtcDateTime;
        var occurred = (n.OccurredAt == default ? n.ChangedDate : n.OccurredAt).UtcDateTime;
        var name = EstateText.Clean(n.Name) ?? EstateText.Clean($"{n.FirstName} {n.LastName}");
        var enquiry = new EnquiryDocument
        {
            Id = enquiries.GetNewId,
            Code = string.Empty,   // numbered once written: a duplicate never takes a number
            Ref = reference,
            OccurredAt = occurred == default ? now : occurred,
            Source = SourceOf(n, meta),
            Form = EstateText.Clean(meta.GetValueOrDefault(EnquiryMetadata.Form)) ?? EstateText.Clean(n.Form),
            Channel = EstateText.Clean(n.Channel),
            LeadId = EstateText.Clean(n.LeadId),
            ContactId = EstateText.Clean(n.ContactId),
            IsNewLead = n.IsNewLead,
            ActivityId = EstateText.Clean(n.ActivityId),
            Name = name,
            Phone = Digits(n.Phone),
            Email = EstateText.Clean(n.Email),
            PropertyId = listing.Property?.Id,
            PropertyCode = listing.Property?.Code,
            PropertyTitle = listing.Property?.Title
                ?? (listing.Project is null && listing.PropertyRef is not null && !IsRealEstateProduct(product) ? EstateText.Clean(product) : null),
            ProjectId = listing.Project?.Id,
            ProjectName = listing.Project?.Name
                ?? (listing.Property is null && listing.ProjectRef is not null && !IsRealEstateProduct(product) ? EstateText.Clean(product) : null),
            UnitTypeId = listing.UnitTypeId,
            UnitTypeName = listing.UnitTypeName,
            Message = n.MessagePreview?.Trim() is { Length: > 0 } message ? message : null,
            Extra = Extras(meta, listing),
            SourceUrl = EstateText.Clean(n.SourceUrl),
            ShareToken = EstateText.Clean(meta.GetValueOrDefault(EnquiryMetadata.ShareToken)),
            AgentId = agentId,
            AgentName = agentName,
            RequirementId = requirementId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        enquiry.Code = PendingCode + enquiry.Id;   // unique until numbered (company_code_unique)
        enquiry.SearchText = EnquiryText.Search(enquiry);
        if (!await enquiries.AddIfNewAsync(enquiry, ct, company))
        {
            logger.LogInformation("Enquiry {Ref} of {CompanyId} was already kept.", reference, company);
            return;
        }
        await NumberAsync(enquiry, company, ct);
    }

    /// <summary>The prefix of an enquiry's code until its number is taken (a moment after it is written).</summary>
    public const string PendingCode = "~";

    /// <summary>Takes the next E-number for a written enquiry (only after the ref's insert won, so a duplicate takes none).</summary>
    private async Task NumberAsync(EnquiryDocument enquiry, string company, CancellationToken ct)
    {
        enquiry.Code = $"E-{await counters.NextAsync(CodeCounter, ct, company):0000}";
        enquiry.SearchText = EnquiryText.Search(enquiry);
        await enquiries.SetCodeAsync(enquiry.Id, enquiry.Code, enquiry.SearchText, ct, company);
    }

    /// <summary>The ref prefix of this service's own captures (share page, "Add enquiry": <see cref="EnquiryRefs"/>).</summary>
    public const string OwnRefPrefix = "estate:";

    /// <summary>The ref prefix of the AI office's captures (zeronova-office's property enquiry).</summary>
    public const string OfficeRefPrefix = "office:";

    /// <summary>Keys only a capture of ours may set: anyone else's (a website form's extra fields) are dropped.</summary>
    private static readonly string[] OwnKeys = [EnquiryMetadata.Estate, EnquiryMetadata.Source, EnquiryMetadata.AgentId, EnquiryMetadata.AgentName];

    /// <summary>
    /// The metadata with the own-capture keys (<c>estate</c>, <c>estateSource</c>, <c>agentId</c>, <c>agentName</c>) kept only
    /// when the capture is ours — its ref starts <see cref="OwnRefPrefix"/>. The AI office's (<see cref="OfficeRefPrefix"/>)
    /// keeps <c>estate</c> and <c>estateSource = AiOffice</c>, never an agent. Anything else loses all four: a website
    /// form's extra fields cannot name the agent, the source or make itself our own (and they are not shown as extras).
    /// </summary>
    public static Dictionary<string, string> Trusted(Dictionary<string, string> meta, string? externalRef)
    {
        var reference = externalRef?.Trim() ?? string.Empty;
        if (reference.StartsWith(OwnRefPrefix, StringComparison.Ordinal))
            return meta;
        var office = reference.StartsWith(OfficeRefPrefix, StringComparison.Ordinal);
        var estate = meta.GetValueOrDefault(EnquiryMetadata.Estate);
        var source = meta.GetValueOrDefault(EnquiryMetadata.Source);
        foreach (var key in OwnKeys)
            meta.Remove(key);
        if (office)
        {
            if (estate is not null)
                meta[EnquiryMetadata.Estate] = estate;
            if (string.Equals(source?.Trim(), nameof(EnquirySource.AiOffice), StringComparison.OrdinalIgnoreCase))
                meta[EnquiryMetadata.Source] = nameof(EnquirySource.AiOffice);
        }
        return meta;
    }

    private async Task<bool> IsRealEstateAsync(LeadEnquiryReceivedNotification n, IReadOnlyDictionary<string, string> meta, string company, CancellationToken ct)
    {
        if (IsRealEstateProduct(meta.GetValueOrDefault(EnquiryMetadata.Product)) || meta.GetValueOrDefault(EnquiryMetadata.Estate) == "1")
            return true;
        if (new[] { EnquiryMetadata.PropertyId, EnquiryMetadata.ProjectId, EnquiryMetadata.UnitTypeId }.Any(k => EstateText.Clean(meta.GetValueOrDefault(k)) is not null))
            return true;
        var form = EstateText.Clean(meta.GetValueOrDefault(EnquiryMetadata.Form)) ?? EstateText.Clean(n.Form);
        return form is not null && EstateWebsiteForms.Contains(form.ToLowerInvariant())
            && await websites.GetDocuments(w => w.Enabled, false, company).AnyAsync(ct);
    }

    private static bool IsRealEstateProduct(string? product) =>
        string.Equals(product?.Trim(), EnquiryMetadata.ProductRealEstate, StringComparison.OrdinalIgnoreCase);

    /// <summary>Our own capture says its source; else the share page's form; else the lead's channel. Called with <see cref="Trusted"/> metadata.</summary>
    public static EnquirySource SourceOf(LeadEnquiryReceivedNotification n, IReadOnlyDictionary<string, string> meta)
    {
        if (meta.GetValueOrDefault(EnquiryMetadata.Estate) == "1" && Enum.TryParse<EnquirySource>(meta.GetValueOrDefault(EnquiryMetadata.Source), true, out var own)
            && Enum.IsDefined(own))
            return own;
        if (string.Equals(meta.GetValueOrDefault(EnquiryMetadata.Form), EnquiryMetadata.SharePageForm, StringComparison.OrdinalIgnoreCase))
            return EnquirySource.SharePage;
        return (n.ActivityChannel ?? n.Channel)?.Trim().ToLowerInvariant() switch
        {
            "website" => EnquirySource.WebsiteForm,
            "whatsapp" => EnquirySource.WhatsApp,
            "call" => EnquirySource.Call,
            "portal" => EnquirySource.Portal,
            _ => EnquirySource.Other,
        };
    }

    /// <summary>The metadata with case-insensitive keys (the first of two keys differing only by case wins).</summary>
    private static Dictionary<string, string> Metadata(Dictionary<string, string>? raw)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw ?? [])
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
                meta.TryAdd(key.Trim(), value);
        return meta;
    }

    /// <summary>The form's own fields (budget, BHK, slot …); a listing reference that was not found is kept too.</summary>
    private static List<EnquiryFieldModel> Extras(IReadOnlyDictionary<string, string> meta, EnquiryListing listing)
    {
        var extra = meta.Where(kv => !KnownKeys.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => new EnquiryFieldModel { Key = kv.Key, Value = kv.Value.Trim() }).ToList();
        if (listing.Property is null && listing.PropertyRef is { } p)
            extra.Add(new EnquiryFieldModel { Key = EnquiryMetadata.PropertyId, Value = p });
        if (listing.Project is null && listing.ProjectRef is { } pj)
            extra.Add(new EnquiryFieldModel { Key = EnquiryMetadata.ProjectId, Value = pj });
        return extra;
    }

    /// <summary>Digits with the country code (a bare 10-digit number is Indian); null when unusable.</summary>
    public static string? Digits(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '0')
            digits = digits[1..];
        if (digits.Length == 10)
            digits = "91" + digits;
        return digits.Length is >= 10 and <= 15 ? digits : null;
    }
}

/// <summary>The listing an enquiry is about, found by id, slug or code; a project unit brings its project and type.</summary>
public sealed record EnquiryListing(PropertyDocument? Property, ProjectDocument? Project, string? UnitTypeId, string? UnitTypeName, string? PropertyRef, string? ProjectRef)
{
    public static readonly EnquiryListing None = new(null, null, null, null, null, null);
}

public static class EnquiryListings
{
    public static async Task<EnquiryListing> ResolveAsync(string? company, string? propertyRef, string? projectRef, string? unitTypeRef, IPropertyRepository properties,
        IProjectRepository projects, IUnitTypeRepository unitTypes, CancellationToken ct)
    {
        propertyRef = EstateText.Clean(propertyRef);
        projectRef = EstateText.Clean(projectRef);
        unitTypeRef = EstateText.Clean(unitTypeRef);

        PropertyDocument? property = null;
        if (propertyRef is not null)
        {
            var slug = propertyRef.ToLowerInvariant();
            var code = propertyRef.ToUpperInvariant();
            property = ObjectId.TryParse(propertyRef, out _)
                ? await properties.GetDocument(propertyRef, true, company).FirstOrDefaultAsync(ct)
                : await properties.GetDocuments(p => p.Slug == slug || p.Code == code, true, company).OrderBy(p => p.IsDeleted).FirstOrDefaultAsync(ct);
        }

        ProjectDocument? project = null;
        if (property?.ProjectId is { } unitProject)
            project = await projects.GetDocument(unitProject, true, company).FirstOrDefaultAsync(ct);
        else if (projectRef is not null)
        {
            var slug = projectRef.ToLowerInvariant();
            var code = projectRef.ToUpperInvariant();
            project = ObjectId.TryParse(projectRef, out _)
                ? await projects.GetDocument(projectRef, true, company).FirstOrDefaultAsync(ct)
                : await projects.GetDocuments(p => p.Slug == slug || p.Code == code, true, company).OrderBy(p => p.IsDeleted).FirstOrDefaultAsync(ct);
        }

        string? typeId = property?.UnitTypeId;
        string? typeName = property?.UnitTypeName;
        if (typeId is null && project is not null && unitTypeRef is not null && ObjectId.TryParse(unitTypeRef, out _))
        {
            var projectId = project.Id;
            if (await unitTypes.GetDocuments(t => t.Id == unitTypeRef && t.ProjectId == projectId, true, company).FirstOrDefaultAsync(ct) is { } type)
                (typeId, typeName) = (type.Id, type.Name);
        }
        return new EnquiryListing(property, project, typeId, typeName, propertyRef, projectRef);
    }
}

internal static class EnquiryText
{
    public static string Search(EnquiryDocument e) => string.Join(' ',
        new[] { e.Code, e.Name, e.Phone, e.Email, e.PropertyCode, e.PropertyTitle, e.ProjectName, e.UnitTypeName }.Where(x => !string.IsNullOrWhiteSpace(x)))
        .ToLowerInvariant();
}
