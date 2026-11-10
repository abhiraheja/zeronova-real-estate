using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Enquiries;
using ZeroNovaRealEstate.Application.Features.Projects;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Requirements;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Features.Visits;
using ZeroNovaRealEstate.Application.Features.Website;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Events;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Settings;
using ZeroNovaRealEstate.Models.Visits;
using ZeroNovaRealEstate.Models.Website;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// Enquiries (C1, D27–D31): zeronova-lead's lead-enquiry-received kept once for Real Estate only, the share page's and
/// staff's enquiries sent as lead-captured-enquiry, working an enquiry, agent scope (D39), the day / week / month stats in
/// India time, and the counts on properties, projects, unit types and the dashboard.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class EnquiryTests(MongoFixture mongo) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Messaging = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    private static readonly string[] Agent = [EstatePermissions.RequirementManage];
    private static readonly string[] Manager = [EstatePermissions.RequirementManage, EstatePermissions.RequirementView];

    private TestHost host = null!;
    private PropertyDto flat = null!;
    private ProjectDto project = null!;
    private UnitTypeDto type = null!;
    private UnitDto unit = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali",
            null, "Sharma Properties", "9876012345")));
        flat = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 70", Bhk = 3, SuperArea = new AreaInput(1650, AreaUnit.SqFt),
            Price = 9500000m, ShowOnWebsite = true,
        }));
        project = await host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = "Green Valley", Kind = ProjectKind.Partner, Locality = "Sector 115", BuilderName = "GV Builders", Categories = [ProjectCategory.Residential],
        }));
        type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput { Name = "3 BHK", Type = PropertyType.Flat, Bhk = 3 }));
        unit = (await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput { ProjectId = project.Id, UnitTypeId = type.Id, Count = 2, Pattern = "A-{n}" })))[0];
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    /// <summary>
    /// zeronova-lead's LeadEnquiryReceivedPayload as it goes on the wire (an envelope with the payload in Message, enums by
    /// name), read the way the consumer reads it.
    /// </summary>
    private static LeadEnquiryReceivedNotification Received(string externalRef, Dictionary<string, string> metadata, string leadId = "lead-1", bool isNew = true,
        DateTimeOffset? at = null, string activityChannel = "Website", string channel = "website", string? assignedTo = "agent-1", string company = CompanyA,
        string phone = "+919811122233")
    {
        var payload = new
        {
            CompanyId = company, LeadId = leadId, ContactId = "contact-" + leadId, NewStatus = "New", NewClassification = "Warm", Source = "Website",
            ChangedBy = "SYSTEM:zeronova-cms", OccurredAt = at ?? DateTimeOffset.UtcNow, Name = "Rahul Verma", FirstName = "Rahul", LastName = "Verma",
            Phone = phone, Email = "rahul@example.test", Tags = new[] { "Website" }, Channel = channel, Form = metadata.GetValueOrDefault("form"),
            Product = metadata.GetValueOrDefault("product"), ReceivedOn = "sharmaproperties.in", NewAssignedTo = assignedTo, NewAssignedToName = "Asha",
            IsNewLead = isNew, ActivityId = "act-" + externalRef, ActivityChannel = activityChannel, ExternalRef = externalRef, Metadata = metadata,
            SourceUrl = "https://sharmaproperties.in/property/x", MessagePreview = "Is it still available?", AssignedTo = assignedTo, AssignedToName = "Asha",
        };
        var json = JsonSerializer.Serialize(new { Date = DateTimeOffset.UtcNow, ChangedBy = "SYSTEM:zeronova-cms", Message = payload, EventName = "lead-enquiry-received" },
            Messaging);
        var n = JsonDocument.Parse(json).RootElement.GetProperty("Message").Deserialize<LeadEnquiryReceivedNotification>(Messaging)!;
        n.MessageId = Guid.NewGuid().ToString("N");
        n.EventType = "lead-enquiry-received";
        n.Source = "zeronova-lead";
        n.ChangedDate = DateTimeOffset.UtcNow;
        return n;
    }

    /// <summary>What zeronova-lead announces back for a capture this service sent.</summary>
    private static LeadEnquiryReceivedNotification Echo(LeadCapturedEnquiryEvent sent, string leadId = "lead-1", string? owner = "agent-1", string activityChannel = "Website") =>
        Received(sent.ExternalRef, new Dictionary<string, string>(sent.Metadata), leadId, activityChannel: activityChannel, assignedTo: owner, company: sent.CompanyId);

    private async Task<IReadOnlyList<EnquiryDto>> All(string company = CompanyA) =>
        (await host.SendAs(company, new GetEnquiriesQuery(new EnquiryFilterInput()))).Items;

    private Task<T> Public<T>(IRequest<T> request)
    {
        T result = default!;
        return host.RunAsJobAsync(async sp => result = await sp.GetRequiredService<IMediator>().Send(request)).ContinueWith(t =>
        {
            t.GetAwaiter().GetResult();
            return result;
        });
    }

    // ── The consumer ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_website_form_about_a_property_is_kept_once_with_its_titles_and_extra_fields_and_a_repeat_is_a_new_enquiry()
    {
        var form = Received("cms-1", new()
        {
            ["form"] = "site-visit", ["product"] = flat.Title, ["propertyId"] = flat.Id, ["budget"] = "1 Cr", ["slot"] = "Sat 11 AM", ["site"] = "sharmaproperties.in",
        });
        await host.PublishAsync(form);
        await host.PublishAsync(form);   // redelivered
        await host.PublishAsync(Received("cms-1", new() { ["form"] = "site-visit", ["propertyId"] = flat.Id }));   // the same capture again

        var e = Assert.Single(await All());
        Assert.Equal(("E-0001", EnquirySource.WebsiteForm, "site-visit", "cms-1", true), (e.Code, e.Source, e.Form, e.Ref, e.IsNewLead));
        Assert.Equal((flat.Id, flat.Code, flat.Title, (string?)null), (e.PropertyId, e.PropertyCode, e.PropertyTitle, e.ProjectId));
        Assert.Equal(("lead-1", "contact-lead-1", "Rahul Verma", "919811122233", "rahul@example.test"), (e.LeadId, e.ContactId, e.Name, e.Phone, e.Email));
        Assert.Equal(("agent-1", "Asha", EnquiryStatus.New, "Is it still available?"), (e.AgentId, e.AgentName, e.Status, e.Message));
        Assert.Equal([("budget", "1 Cr"), ("slot", "Sat 11 AM")], e.Extra.Select(x => (x.Key, x.Value)).OrderBy(x => x.Key));

        // The same person asks again (their open lead): a second enquiry; the website may name the listing by its slug.
        await host.PublishAsync(Received("cms-2", new() { ["form"] = "callback", ["propertyId"] = flat.WebsiteSlug! }, isNew: false));
        var repeat = (await All())[0];
        Assert.Equal(("E-0002", false, flat.Id, "callback"), (repeat.Code, repeat.IsNewLead, repeat.PropertyId, repeat.Form));
    }

    [Fact]
    public async Task Only_real_estate_enquiries_are_kept()
    {
        await host.PublishAsync(Received("t-1", new() { ["form"] = "contact", ["product"] = "IELTS batch" }));   // another product
        await host.PublishAsync(Received("t-2", new() { ["form"] = "contact" }));
        await host.PublishAsync(Received("t-3", new() { ["form"] = "project-enquiry" }));   // the website's own form, but its Real Estate website is off
        Assert.Empty(await All());

        await host.PublishAsync(Received("r-1", new() { ["product"] = "REAL_ESTATE", ["form"] = "contact" }));
        await host.PublishAsync(Received("r-2", new() { ["projectId"] = project.Code }));   // by code
        await host.PublishAsync(Received("r-3", new() { ["propertyId"] = "6a00000000000000000000ff", ["product"] = "Some old listing" }));   // not found: kept, raw id kept
        await host.SendAs(CompanyA, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(true, null)));
        await host.PublishAsync(Received("r-4", new() { ["form"] = "post-property", ["owner_city"] = "Mohali" }));
        await host.PublishAsync(Received("r-5", new() { ["product"] = "REAL_ESTATE" }, company: CompanyB));   // another company's

        var kept = await All();
        Assert.Equal(["r-4", "r-3", "r-2", "r-1"], kept.Select(e => e.Ref));
        var byCode = kept.Single(e => e.Ref == "r-2");
        Assert.Equal((project.Id, "Green Valley", EnquirySource.WebsiteForm), (byCode.ProjectId, byCode.ProjectName, byCode.Source));
        var missing = kept.Single(e => e.Ref == "r-3");
        Assert.Equal(((string?)null, "Some old listing"), (missing.PropertyId, missing.PropertyTitle));
        Assert.Contains(missing.Extra, x => x is { Key: "propertyId", Value: "6a00000000000000000000ff" });
        Assert.Single(await All(CompanyB));
    }

    [Fact]
    public async Task A_unit_brings_its_project_and_type_and_the_channels_give_the_source()
    {
        await host.PublishAsync(Received("u-1", new() { ["propertyId"] = unit.Id }, activityChannel: "WhatsApp", channel: "whatsapp"));
        await host.PublishAsync(Received("u-2", new() { ["projectId"] = project.Id, ["unitTypeId"] = type.Id }, activityChannel: "Call", channel: "call"));
        await host.PublishAsync(Received("u-3", new() { ["product"] = "REAL_ESTATE" }, activityChannel: "Internal", channel: "manual"));

        var rows = (await All()).ToDictionary(e => e.Ref);
        Assert.Equal((EnquirySource.WhatsApp, project.Id, type.Id, "3 BHK", unit.Code), (rows["u-1"].Source, rows["u-1"].ProjectId, rows["u-1"].UnitTypeId,
            rows["u-1"].UnitTypeName, rows["u-1"].PropertyCode));
        Assert.Equal((EnquirySource.Call, type.Id), (rows["u-2"].Source, rows["u-2"].UnitTypeId));
        Assert.Equal(EnquirySource.Other, rows["u-3"].Source);
    }

    // ── The share page ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_share_page_sends_its_enquiry_to_the_lead_service_counts_it_and_refuses_too_many()
    {
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, new RequirementInput
        {
            LeadId = "lead-1", LeadName = "Rahul", Purpose = PropertyPurpose.Sale, Types = [PropertyType.Flat],
        }));
        var share = await host.SendAs(CompanyA, new CreateShareCommand(flat.Id, requirement.Id));

        var ack = await Public(new EnquireShareCommand(share.Token, " Neha  Gupta ", "098111 22233", "Can I visit on Sunday?"));
        Assert.True(ack.Received);
        var (sent, changedBy) = Assert.Single(host.Captures.Sent);
        Assert.Equal((CompanyA, "Website", "Website", "Neha Gupta", "+919811122233", "Can I visit on Sunday?", share.Url, "user-test", "share-page"),
            (sent.CompanyId, sent.LeadSource, sent.Channel, sent.Name, sent.Phone, sent.Message, sent.SourceUrl, sent.AssignTo, changedBy));
        Assert.StartsWith("estate:", sent.ExternalRef);
        Assert.Null(sent.LeadId);   // the person is found by phone: a forwarded link reaches whoever enquires
        Assert.Equal(new Dictionary<string, string>
        {
            ["product"] = "REAL_ESTATE", ["estate"] = "1", ["form"] = "share-page", ["shareToken"] = share.Token, ["propertyId"] = flat.Id,
            ["requirementId"] = requirement.Id, ["agentId"] = "user-test",
        }, sent.Metadata.OrderBy(kv => kv.Key).ToDictionary());

        // On the wire: zeronova-lead's field names — LeadSource, never Source (the envelope's).
        var wire = JsonDocument.Parse(JsonSerializer.Serialize(sent, Messaging)).RootElement;
        Assert.Equal("Website", wire.GetProperty("LeadSource").GetString());
        Assert.False(wire.TryGetProperty("Source", out _));
        Assert.Equal(sent.ExternalRef, wire.GetProperty("ExternalRef").GetString());

        Assert.Equal(1, Assert.Single(await host.SendAs(CompanyA, new GetSharesQuery(flat.Id))).Enquiries);

        // zeronova-lead records it and announces it back: a share-page enquiry, linked to the requirement it was shared for.
        await host.PublishAsync(Echo(sent));
        var e = Assert.Single(await All());
        Assert.Equal((EnquirySource.SharePage, "share-page", flat.Id, requirement.Id, "user-test", EnquiryStatus.New),
            (e.Source, e.Form, e.PropertyId, e.RequirementId, e.AgentId, e.Status));

        // The guard: five an hour per link.
        for (var i = 0; i < 4; i++)
            await Public(new EnquireShareCommand(share.Token, "Neha", "9811122233", null));
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EnquireShareCommand(share.Token, "Neha", "9811122233", null)));
        Assert.Equal(5, host.Captures.Sent.Count);
        Assert.Equal(5, Assert.Single(await host.SendAs(CompanyA, new GetSharesQuery(flat.Id))).Enquiries);

        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EnquireShareCommand(share.Token, "Neha", "12345", null)));   // not an Indian mobile
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EnquireShareCommand("no-such-token", "Neha", "9811122233", null)));
    }

    [Fact]
    public async Task A_project_share_sends_the_project_and_no_agent_without_a_requirement()
    {
        var share = await host.SendAs(CompanyA, new CreateProjectShareCommand(project.Id, null));
        await Public(new EnquireShareCommand(share.Token, "Neha", "+91 98111 22233", null));

        var (sent, _) = Assert.Single(host.Captures.Sent);
        Assert.Equal((share.Url, (string?)null, project.Id), (sent.SourceUrl, sent.AssignTo, sent.Metadata["projectId"]));
        Assert.Contains("/pj/", sent.SourceUrl);
        Assert.False(sent.Metadata.ContainsKey("propertyId") || sent.Metadata.ContainsKey("requirementId") || sent.Metadata.ContainsKey("agentId"));

        await host.PublishAsync(Echo(sent, owner: "agent-9"));
        var e = Assert.Single(await All());
        Assert.Equal((EnquirySource.SharePage, project.Id, "agent-9"), (e.Source, e.ProjectId, e.AgentId));   // the lead's owner
    }

    // ── Staff ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_enquiry_sends_a_capture_and_the_row_appears_by_its_ref()
    {
        var ack = await host.SendAsMember(CompanyA, "agent-7", Agent, new AddEnquiryCommand(new AddEnquiryInput
        {
            Name = "Walk-in Kumar", Phone = "98111 22233", ProjectId = project.Id, UnitTypeId = type.Id, Source = EnquirySource.WalkIn, Message = "Wants 3 BHK",
        }));
        var (sent, changedBy) = Assert.Single(host.Captures.Sent);
        Assert.Equal((ack.Ref, "Manual", "Internal", "+919811122233", "agent-7", (string?)null, "agent-7"),
            (sent.ExternalRef, sent.LeadSource, sent.Channel, sent.Phone, sent.AssignTo, sent.LeadId, changedBy));
        Assert.Equal(new Dictionary<string, string>
        {
            ["agentId"] = "agent-7", ["estate"] = "1", ["estateSource"] = "WalkIn", ["product"] = "REAL_ESTATE", ["projectId"] = project.Id, ["unitTypeId"] = type.Id,
        }, sent.Metadata.OrderBy(kv => kv.Key).ToDictionary());
        Assert.Null(await host.SendAsMember(CompanyA, "agent-7", Agent, new GetEnquiryByRefQuery(ack.Ref)));   // still being recorded

        // The lead already had another owner: the enquiry stays with the agent who took it.
        await host.PublishAsync(Echo(sent, owner: "agent-1", activityChannel: "Internal"));
        var e = await host.SendAsMember(CompanyA, "agent-7", Agent, new GetEnquiryByRefQuery(ack.Ref));
        Assert.Equal((EnquirySource.WalkIn, "agent-7", project.Id, type.Id, "3 BHK"), (e!.Source, e.AgentId, e.ProjectId, e.UnitTypeId, e.UnitTypeName));

        // A call and a WhatsApp: their lead sources and channels; a picked lead goes as it is.
        await host.SendAs(CompanyA, new AddEnquiryCommand(new AddEnquiryInput { LeadId = "lead-5", PropertyId = flat.Id, Source = EnquirySource.Call }));
        await host.SendAs(CompanyA, new AddEnquiryCommand(new AddEnquiryInput { LeadId = "lead-5", Source = EnquirySource.WhatsApp }));
        var sends = host.Captures.Sent.Skip(1).Select(x => x.Event).ToList();
        Assert.Equal([("ManualCall", "Call", "lead-5", "user-test"), ("WhatsApp", "WhatsApp", "lead-5", "user-test")],
            sends.Select(s => (s.LeadSource, s.Channel, s.LeadId!, s.AssignTo!)));

        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-7", Agent, new AddEnquiryCommand(new AddEnquiryInput
        {
            LeadId = "lead-5", AgentId = "agent-8",   // another agent: managers only
        })));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new AddEnquiryCommand(new AddEnquiryInput { LeadId = "lead-5", Source = EnquirySource.SharePage })));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new AddEnquiryCommand(new AddEnquiryInput { Name = "No phone" })));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new AddEnquiryCommand(new AddEnquiryInput { LeadId = "lead-5", UnitTypeId = type.Id })));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyB, new AddEnquiryCommand(new AddEnquiryInput { LeadId = "lead-5", PropertyId = flat.Id })));
        var managerAck = await host.SendAsMember(CompanyA, "boss", Manager, new AddEnquiryCommand(new AddEnquiryInput { LeadId = "lead-5", AgentId = "agent-8" }));
        Assert.Equal("agent-8", host.Captures.Sent.Single(x => x.Event.ExternalRef == managerAck.Ref).Event.AssignTo);
    }

    // ── Working an enquiry ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_enquiry_is_worked_closed_with_a_reason_and_moved_on_by_its_requirement_and_visit()
    {
        await host.PublishAsync(Received("w-1", new() { ["propertyId"] = flat.Id }, assignedTo: "user-test"));
        var e = Assert.Single(await All());

        var contacted = await host.SendAs(CompanyA, new UpdateEnquiryCommand(e.Id, new UpdateEnquiryInput { Status = EnquiryStatus.Contacted }));
        Assert.NotNull(contacted.ContactedAt);
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new UpdateEnquiryCommand(e.Id, new UpdateEnquiryInput { Status = EnquiryStatus.Closed })));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new UpdateEnquiryCommand(e.Id,
            new UpdateEnquiryInput { Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.NotAvailable })));   // what was wanted?
        var closed = await host.SendAs(CompanyA, new UpdateEnquiryCommand(e.Id, new UpdateEnquiryInput
        {
            Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.NotAvailable, Wanted = "3 BHK in Sector 70 under 90 lakh", CloseNote = "Will wait",
        }));
        Assert.Equal((EnquiryStatus.Closed, EnquiryCloseReason.NotAvailable, "3 BHK in Sector 70 under 90 lakh"), (closed.Status, closed.CloseReason, closed.Wanted));
        Assert.NotNull(closed.ClosedAt);
        Assert.Empty(host.Captures.Sent);   // closing never touches the lead
        Assert.Empty(host.Contacts.Sent);

        var reopened = await host.SendAs(CompanyA, new UpdateEnquiryCommand(e.Id, new UpdateEnquiryInput { Status = EnquiryStatus.New }));
        Assert.Equal((EnquiryStatus.New, (EnquiryCloseReason?)null, (string?)null), (reopened.Status, reopened.CloseReason, reopened.Wanted));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new UpdateEnquiryCommand(e.Id, new UpdateEnquiryInput { Status = EnquiryStatus.VisitBooked })));

        // "Add requirement" from the enquiry; another customer's requirement cannot take it.
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveRequirementCommand(null, new RequirementInput
        {
            LeadId = "lead-2", Purpose = PropertyPurpose.Sale, EnquiryId = e.Id,
        })));
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, new RequirementInput
        {
            LeadId = "lead-1", LeadName = "Rahul", Purpose = PropertyPurpose.Sale, Types = [PropertyType.Flat], EnquiryId = e.Id,
        }));
        var withRequirement = await host.SendAs(CompanyA, new GetEnquiryQuery(e.Id));
        Assert.Equal((EnquiryStatus.RequirementAdded, requirement.Id), (withRequirement.Status, withRequirement.RequirementId));

        var visit = await host.SendAs(CompanyA, new ScheduleVisitCommand(new VisitInput
        {
            PropertyId = flat.Id, RequirementId = requirement.Id, LeadId = "lead-1", At = DateTimeOffset.UtcNow.AddDays(1), EnquiryId = e.Id,
        }));
        var booked = await host.SendAs(CompanyA, new GetEnquiryQuery(e.Id));
        Assert.Equal((EnquiryStatus.VisitBooked, visit.Id, requirement.Id), (booked.Status, booked.VisitId, booked.RequirementId));

        // Linking by hand works too; a closed enquiry keeps its status and only takes the link.
        await host.PublishAsync(Received("w-2", new() { ["propertyId"] = flat.Id }, assignedTo: "user-test"));
        var second = (await All()).Single(x => x.Ref == "w-2");
        await host.SendAs(CompanyA, new UpdateEnquiryCommand(second.Id, new UpdateEnquiryInput { Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.Duplicate }));
        var linked = await host.SendAs(CompanyA, new UpdateEnquiryCommand(second.Id, new UpdateEnquiryInput { RequirementId = requirement.Id }));
        Assert.Equal((EnquiryStatus.Closed, requirement.Id), (linked.Status, linked.RequirementId));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new UpdateEnquiryCommand(second.Id, new UpdateEnquiryInput { CloseReason = EnquiryCloseReason.Spam })));
    }

    // ── Scope (D39) ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_agent_sees_and_works_their_own_enquiries_and_a_manager_everyones()
    {
        await host.PublishAsync(Received("s-1", new() { ["propertyId"] = flat.Id }, assignedTo: "agent-1"));
        await host.PublishAsync(Received("s-2", new() { ["propertyId"] = flat.Id }, assignedTo: "agent-2"));
        await host.PublishAsync(Received("s-3", new() { ["propertyId"] = flat.Id }, assignedTo: null));

        var mine = await host.SendAsMember(CompanyA, "agent-1", Agent, new GetEnquiriesQuery(new EnquiryFilterInput { AgentId = "agent-2" }));
        Assert.Equal(["s-1"], mine.Items.Select(e => e.Ref));   // an agent's filter cannot reach another agent
        var all = await host.SendAsMember(CompanyA, "boss", Manager, new GetEnquiriesQuery(new EnquiryFilterInput()));
        Assert.Equal(3, all.Total);
        var two = await host.SendAsMember(CompanyA, "boss", Manager, new GetEnquiriesQuery(new EnquiryFilterInput { AgentId = "agent-2" }));
        var other = Assert.Single(two.Items);

        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-1", Agent, new GetEnquiryQuery(other.Id)));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-1", Agent, new UpdateEnquiryCommand(other.Id,
            new UpdateEnquiryInput { Status = EnquiryStatus.Contacted })));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-1", Agent, new GetEnquiryByRefQuery("s-2")));

        // A manager gives the unassigned one to agent-1, who then sees it.
        var unassigned = all.Items.Single(e => e.Ref == "s-3");
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-1", Agent, new UpdateEnquiryCommand(unassigned.Id,
            new UpdateEnquiryInput { AgentId = "agent-1" })));
        await host.SendAsMember(CompanyA, "boss", Manager, new UpdateEnquiryCommand(unassigned.Id, new UpdateEnquiryInput { AgentId = "agent-1", AgentName = "Asha" }));
        Assert.Equal(2, (await host.SendAsMember(CompanyA, "agent-1", Agent, new GetEnquiriesQuery(new EnquiryFilterInput()))).Total);

        var stats = await host.SendAsMember(CompanyA, "agent-1", Agent, new GetEnquiryStatsQuery(new EnquiryFilterInput(), EnquiryBucket.Day, null, null));
        Assert.Equal(2, stats.Total);
        var dashboard = await host.SendAsMember(CompanyA, "agent-2", [EstatePermissions.RequirementManage, EstatePermissions.PropertyView], new GetEstateDashboardQuery());
        Assert.Equal((1, 1, 1), (dashboard.EnquiriesToday, dashboard.EnquiriesThisWeek, dashboard.OpenEnquiries));

        // Search by name, phone digits or code; other filters.
        Assert.Equal(3, (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput { Text = "rahul 98111" }))).Total);
        Assert.Equal(1, (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput { Text = "e-0002" }))).Total);
        Assert.Equal(0, (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput { Text = "neha" }))).Total);
        Assert.Equal(3, (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput { PropertyId = flat.Id, LeadId = "lead-1", Source = EnquirySource.WebsiteForm }))).Total);
        Assert.Equal(0, (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput { Status = EnquiryStatus.Closed }))).Total);
        Assert.Equal(2, (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput(), 1, 2))).Items.Count);
    }

    // ── Stats (D30) ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stats_count_india_days_weeks_from_monday_and_months()
    {
        // 23:50 and 00:10 India time either side of three midnights: Sat 31 Oct → Sun 1 Nov (a new month), Sun 8 Nov → Mon 9 Nov (a new week).
        await host.PublishAsync(Received("a", new() { ["propertyId"] = flat.Id }, at: new DateTimeOffset(2026, 10, 31, 18, 20, 0, TimeSpan.Zero)));
        await host.PublishAsync(Received("b", new() { ["propertyId"] = flat.Id }, at: new DateTimeOffset(2026, 10, 31, 18, 40, 0, TimeSpan.Zero)));
        await host.PublishAsync(Received("c", new() { ["propertyId"] = flat.Id }, at: new DateTimeOffset(2026, 11, 8, 18, 20, 0, TimeSpan.Zero)));
        await host.PublishAsync(Received("d", new() { ["propertyId"] = flat.Id }, at: new DateTimeOffset(2026, 11, 8, 18, 40, 0, TimeSpan.Zero)));
        await host.PublishAsync(Received("e", new() { ["propertyId"] = flat.Id }, at: new DateTimeOffset(2026, 11, 9, 5, 0, 0, TimeSpan.Zero),
            activityChannel: "Call", channel: "call"));
        await host.PublishAsync(Received("x", new() { ["propertyId"] = flat.Id }, at: new DateTimeOffset(2026, 9, 30, 18, 29, 0, TimeSpan.Zero)));   // 23:59 30 Sep
        var e = (await All()).Single(x => x.Ref == "e");
        await host.SendAs(CompanyA, new UpdateEnquiryCommand(e.Id, new UpdateEnquiryInput { Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.Converted }));

        var days = await host.SendAs(CompanyA, new GetEnquiryStatsQuery(new EnquiryFilterInput(), EnquiryBucket.Day, "2026-10-31", "2026-11-09"));
        Assert.Equal(("2026-10-31", "2026-11-09", 10, 5), (days.From, days.To, days.Buckets.Count, days.Total));
        var perDay = days.Buckets.Where(b => b.Total > 0).Select(b => (b.Start, b.Total)).ToList();
        Assert.Equal([("2026-10-31", 1), ("2026-11-01", 1), ("2026-11-08", 1), ("2026-11-09", 2)], perDay);
        var ninth = days.Buckets.Single(b => b.Start == "2026-11-09");
        Assert.Equal((1, 1), (ninth.Open, ninth.Closed));
        Assert.Equal([(EnquirySource.WebsiteForm, 1), (EnquirySource.Call, 1)], ninth.BySource.Select(s => (s.Source, s.Count)));
        Assert.Equal([(EnquirySource.WebsiteForm, 4), (EnquirySource.Call, 1)], days.BySource.Select(s => (s.Source, s.Count)));
        Assert.Equal((4, 1), (days.Open, days.Closed));

        var weeks = await host.SendAs(CompanyA, new GetEnquiryStatsQuery(new EnquiryFilterInput(), EnquiryBucket.Week, "2026-10-31", "2026-11-09"));
        Assert.Equal(("2026-10-26", "2026-11-15"), (weeks.From, weeks.To));   // whole weeks, Monday to Sunday
        Assert.Equal([("2026-10-26", 2), ("2026-11-02", 1), ("2026-11-09", 2)], weeks.Buckets.Select(b => (b.Start, b.Total)));

        var months = await host.SendAs(CompanyA, new GetEnquiryStatsQuery(new EnquiryFilterInput(), EnquiryBucket.Month, "2026-09-15", "2026-11-09"));
        Assert.Equal(("2026-09-01", "2026-11-30"), (months.From, months.To));
        Assert.Equal([("2026-09-01", 1), ("2026-10-01", 1), ("2026-11-01", 4)], months.Buckets.Select(b => (b.Start, b.Total)));

        var calls = await host.SendAs(CompanyA, new GetEnquiryStatsQuery(new EnquiryFilterInput { Source = EnquirySource.Call }, EnquiryBucket.Month, "2026-11-01", "2026-11-01"));
        Assert.Equal(1, calls.Total);
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new GetEnquiryStatsQuery(new EnquiryFilterInput(), EnquiryBucket.Day, "2026-11-09", "2026-11-01")));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new GetEnquiryStatsQuery(new EnquiryFilterInput(), EnquiryBucket.Day, "2020-01-01", "2026-11-01")));
    }

    // ── Counts (D30) ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Properties_projects_and_unit_types_count_their_enquiries()
    {
        var now = DateTimeOffset.UtcNow;
        await host.PublishAsync(Received("p-1", new() { ["propertyId"] = flat.Id }, at: now.AddDays(-3)));
        await host.PublishAsync(Received("p-2", new() { ["propertyId"] = flat.Id }, at: now.AddDays(-20), activityChannel: "Call", channel: "call"));
        await host.PublishAsync(Received("p-3", new() { ["propertyId"] = flat.Id, ["form"] = "share-page" }, at: now.AddDays(-60)));
        await host.PublishAsync(Received("p-4", new() { ["propertyId"] = flat.Id }, at: now.AddDays(-1)));
        var spam = (await All()).Single(x => x.Ref == "p-4");
        await host.SendAs(CompanyA, new UpdateEnquiryCommand(spam.Id, new UpdateEnquiryInput { Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.Spam }));
        await host.PublishAsync(Received("u-1", new() { ["propertyId"] = unit.Id }, at: now.AddDays(-2)));
        await host.PublishAsync(Received("u-2", new() { ["projectId"] = project.Id, ["unitTypeId"] = type.Id }, at: now.AddDays(-10)));
        await host.PublishAsync(Received("u-3", new() { ["projectId"] = project.Id }, at: now.AddDays(-40)));

        var counts = (await host.SendAs(CompanyA, new GetPropertyQuery(flat.Id))).Enquiries;
        Assert.Equal((3, 1, 2), (counts.Total, counts.Last7Days, counts.Last30Days));   // spam left out
        Assert.Equal([(EnquirySource.WebsiteForm, 1), (EnquirySource.SharePage, 1), (EnquirySource.Call, 1)], counts.BySource.Select(s => (s.Source, s.Count)));
        var listed = (await host.SendAs(CompanyA, new GetPropertiesQuery(new PropertySearchInput()))).Items.Single(p => p.Id == flat.Id);
        Assert.Equal(3, listed.Enquiries.Total);

        var p = await host.SendAs(CompanyA, new GetProjectQuery(project.Id));
        Assert.Equal((3, 1, 2), (p.Enquiries.Total, p.Enquiries.Last7Days, p.Enquiries.Last30Days));   // its unit's, its type's and its own
        Assert.Equal((2, 1, 2), (p.UnitTypes.Single().Enquiries.Total, p.UnitTypes.Single().Enquiries.Last7Days, p.UnitTypes.Single().Enquiries.Last30Days));
        var saved = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, type.Id, new UnitTypeInput { Name = "3 BHK", Type = PropertyType.Flat, Bhk = 3 }));
        Assert.Equal(2, saved.Enquiries.Total);
        var unitCounts = (await host.SendAs(CompanyA, new GetPropertyQuery(unit.Id))).Enquiries;
        Assert.Equal(1, unitCounts.Total);

        var other = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Rent, Type = PropertyType.Shop, Locality = "VIP Road", BuiltUpArea = new AreaInput(400, AreaUnit.SqFt), Rent = 45000m,
        }));
        Assert.Equal((0, 0, 0, 0), (other.Enquiries.Total, other.Enquiries.Last7Days, other.Enquiries.Last30Days, other.Enquiries.BySource.Count));

        var dashboard = await host.SendAs(CompanyA, new GetEstateDashboardQuery());
        Assert.Equal(6, dashboard.OpenEnquiries);   // every one but the spam
        Assert.True(dashboard.EnquiriesThisWeek >= dashboard.EnquiriesToday);
    }
}
