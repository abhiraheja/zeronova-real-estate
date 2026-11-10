using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Customers;
using ZeroNovaRealEstate.Application.Features.Enquiries;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Features.Projects;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Requirements;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Features.Website;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Base;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Customers;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Settings;
using ZeroNovaRealEstate.Models.Website;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// Watched demand (C3, D31, D33, D34): close reason codes (and the old rows' notes), an enquiry not available becoming a
/// watched requirement, the watch sweep, revived matches (single properties and project unit types) with their own bell and
/// the highlight, never re-offering a shown or rejected match, the one-click message (template through zeronova-social and
/// the wa.me draft, the failure coming back), the unmet-demand report with the website's empty searches, and the projects
/// list's enquiry counts.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class WatchedDemandTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        await Settings();
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private Task<EstateSettingsDto> Settings(int? watchDays = null, string? template = null, string? language = null) =>
        host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali",
            "PBRERA-SAS79-REA0123", "Sharma Properties", "9876012345", WatchDays: watchDays, MatchTemplateName: template, MatchTemplateLanguage: language)));

    private Task<RequirementDto> Requirement(string lead = "lead-1", string contact = "contact-1", string name = "Rahul Verma", string locality = "Sector 70",
        decimal budgetMax = 10000000m) =>
        host.SendAs(CompanyA, new SaveRequirementCommand(null, new RequirementInput
        {
            LeadId = lead, ContactId = contact, LeadName = name, LeadPhone = "9811122233", Purpose = PropertyPurpose.Sale, Types = [PropertyType.Flat],
            Localities = [locality], Bhk = [3], BudgetMin = 6000000m, BudgetMax = budgetMax,
        }));

    private Task<RequirementDto> Close(string id, RequirementStatus status, string? note, RequirementCloseReason? code) =>
        host.SendAs(CompanyA, new CloseRequirementCommand(id, new CloseRequirementInput(status, note, code)));

    private Task<PropertyDto> Flat(string locality = "Sector 70", decimal price = 9500000m, int bhk = 3, bool website = false) =>
        host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = locality, Bhk = bhk, SuperArea = new AreaInput(1650, AreaUnit.SqFt),
            Price = price, ShowOnWebsite = website,
        }));

    private Task<IReadOnlyList<MatchDto>> Matches(string requirementId) => host.SendAs(CompanyA, new GetMatchesQuery(null, requirementId, null));

    private async Task<IReadOnlyList<string?>> Highlighted() =>
        [.. (await host.SendAs(CompanyA, new GetCustomersQuery(new EstateCustomerFilterInput { Highlighted = true }))).Items.Select(c => c.ContactId)];

    /// <summary>Changes a stored row directly (to stand for an old row, or to move its dates back).</summary>
    private Task Change<T>(string id, Action<T> change) where T : CompanyDocumentBase =>
        host.WithAsync(CompanyA, async sp =>
        {
            var collection = sp.GetRequiredService<IMongoDatabase>().GetCollection<T>(typeof(T) == typeof(RequirementDocument) ? "re_requirements" : "re_matches");
            var row = await collection.Find(x => x.Id == id).FirstAsync();
            change(row);
            await collection.ReplaceOneAsync(x => x.Id == id, row);
        });

    private static LeadEnquiryReceivedNotification Enquiry(string externalRef, Dictionary<string, string> metadata, string lead = "lead-1", string contact = "contact-1",
        string name = "Rahul Verma") =>
        new()
        {
            CompanyId = CompanyA, LeadId = lead, ContactId = contact, Name = name, Phone = "+919811122233", OccurredAt = DateTimeOffset.UtcNow,
            Channel = "website", ActivityChannel = "Website", ExternalRef = externalRef, ActivityId = "act-" + externalRef, AssignedTo = "user-test",
            AssignedToName = "Asha", Metadata = metadata, IsNewLead = true, MessageId = Guid.NewGuid().ToString("N"), EventType = "lead-enquiry-received",
            Source = "zeronova-lead", ChangedDate = DateTimeOffset.UtcNow,
        };

    private Task<T> Public<T>(IRequest<T> request)
    {
        T result = default!;
        return host.RunAsJobAsync(async sp => result = await sp.GetRequiredService<IMediator>().Send(request)).ContinueWith(t =>
        {
            t.GetAwaiter().GetResult();
            return result;
        });
    }

    [Fact]
    public async Task Close_reasons_are_codes_not_available_watches_and_old_rows_are_read_by_their_note()
    {
        var elsewhere = await Close((await Requirement()).Id, RequirementStatus.Closed, "Took a flat in Kharar", RequirementCloseReason.BoughtElsewhere);
        Assert.Equal((RequirementStatus.Closed, RequirementCloseReason.BoughtElsewhere, "Took a flat in Kharar"),
            (elsewhere.Status, elsewhere.CloseReasonCode, elsewhere.CloseReason));
        Assert.NotNull(elsewhere.ClosedAt);
        Assert.Null(elsewhere.WatchUntil);

        // A close without a code is Other: the note's words are read only on rows from before C3.
        var noCode = await Close((await Requirement()).Id, RequirementStatus.Closed, "nahi mila", null);
        Assert.Equal((RequirementStatus.Closed, RequirementCloseReason.Other), (noCode.Status, noCode.CloseReasonCode));

        // Not available watches it, for the dealer's days (90 by default; 30 once set); closed or lost alike.
        var watched = await Close((await Requirement()).Id, RequirementStatus.Closed, "Sector 70 me nahi mila", RequirementCloseReason.NotAvailable);
        Assert.Equal((RequirementStatus.Watching, RequirementCloseReason.NotAvailable), (watched.Status, watched.CloseReasonCode));
        Assert.InRange((watched.WatchUntil!.Value - DateTimeOffset.UtcNow).TotalDays, 89.9, 90.1);
        Assert.Equal(30, (await Settings(watchDays: 30)).WatchDays);
        Assert.Equal(30, (await Settings()).WatchDays);   // null keeps
        var lost = await Close((await Requirement()).Id, RequirementStatus.Lost, null, RequirementCloseReason.NotAvailable);
        Assert.Equal(RequirementStatus.Watching, lost.Status);
        Assert.InRange((lost.WatchUntil!.Value - DateTimeOffset.UtcNow).TotalDays, 29.9, 30.1);
        Assert.Equal(RequirementStatus.Watching, (await host.SendAs(CompanyA, new CloseRequirementCommand((await Requirement()).Id,
            new CloseRequirementInput(RequirementStatus.Watching, null)))).Status);

        // A code needs a close; a watch is only for not available. Reopening clears it all.
        await Assert.ThrowsAsync<ValidatorException>(() => Close(watched.Id, RequirementStatus.Active, null, RequirementCloseReason.Other));
        await Assert.ThrowsAsync<ValidatorException>(() => Close(watched.Id, RequirementStatus.Watching, null, RequirementCloseReason.BoughtElsewhere));
        var reopened = await Close(watched.Id, RequirementStatus.Active, null, null);
        Assert.Equal((RequirementStatus.Active, null, null, null, null),
            (reopened.Status, reopened.CloseReasonCode, reopened.CloseReason, reopened.ClosedAt, reopened.WatchUntil));

        // Rows closed before C3 (no code): a note saying not available is unmet demand, another note is not.
        var oldNotAvailable = await Requirement("lead-7", "contact-7");
        var oldOther = await Requirement("lead-7", "contact-7");
        foreach (var (id, note) in new[] { (oldNotAvailable.Id, "Not available in this budget"), (oldOther.Id, "Changed his mind") })
            await Change<RequirementDocument>(id, r =>
            {
                r.Status = RequirementStatus.Closed;
                r.CloseReason = note;
                r.CloseReasonCode = null;
                r.ClosedAt = null;
            });
        var dashboard = await host.SendAs(CompanyA, new GetCustomerQuery("contact-7", null));
        Assert.Equal([oldNotAvailable.Id], dashboard.Unmet.Select(u => u.Id));
        var report = await host.SendAs(CompanyA, new GetDemandReportQuery(null, null, [DemandGroupBy.Locality]));
        Assert.Equal(1, report.Totals.Single(t => t.Source == DemandSource.ClosedNotAvailable).Count);
    }

    [Fact]
    public async Task An_enquiry_closed_not_available_becomes_one_watched_requirement_of_its_lead()
    {
        await host.PublishAsync(Enquiry("cms-1", new()
        {
            ["product"] = "REAL_ESTATE", ["form"] = "requirement", ["budget"] = "50-80 lakh", ["bhk"] = "3", ["purpose"] = "Buy", ["propertyType"] = "Flat",
            ["locality"] = "Sector 70",
        }));
        var enquiry = Assert.Single((await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput()))).Items);
        var closed = await host.SendAs(CompanyA, new UpdateEnquiryCommand(enquiry.Id, new UpdateEnquiryInput
        {
            Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.NotAvailable, Wanted = "3 BHK flat in Sector 70, 50-80 lakh",
        }));
        Assert.Equal(EnquiryStatus.Closed, closed.Status);
        var watched = await host.SendAs(CompanyA, new GetRequirementQuery(closed.RequirementId!));
        Assert.Equal((RequirementStatus.Watching, RequirementCloseReason.NotAvailable, enquiry.Id, "lead-1", "contact-1", "Rahul Verma", "919811122233", "user-test"),
            (watched.Status, watched.CloseReasonCode, watched.EnquiryId, watched.LeadId, watched.ContactId, watched.LeadName, watched.LeadPhone, watched.AgentId));
        Assert.Equal((PropertyPurpose.Sale, PropertyType.Flat, "Mohali", "Sector 70", 3, 5000000m, 8000000m),
            (watched.Purpose, Assert.Single(watched.Types), watched.City, Assert.Single(watched.Localities), Assert.Single(watched.Bhk), watched.BudgetMin!.Value,
                watched.BudgetMax!.Value));
        Assert.Equal("3 BHK flat in Sector 70, 50-80 lakh", watched.Notes);

        // Reopened and closed again: the same requirement is watched again, never a second one.
        await host.SendAs(CompanyA, new UpdateEnquiryCommand(enquiry.Id, new UpdateEnquiryInput { Status = EnquiryStatus.Contacted }));
        var again = await host.SendAs(CompanyA, new UpdateEnquiryCommand(enquiry.Id, new UpdateEnquiryInput
        {
            Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.NotAvailable, Wanted = "3 BHK flat in Sector 70 or 71",
        }));
        Assert.Equal(watched.Id, again.RequirementId);
        Assert.Single(await host.SendAs(CompanyA, new GetRequirementsQuery(null, "lead-1", null, null)));

        // From a listing: a sold property gives the purpose, type, place, BHK and its price as the budget.
        var sold = await Flat("Sector 71", 7000000m, 2);
        await host.SendAs(CompanyA, new ConfirmPropertyCommand(sold.Id, new ConfirmPropertyInput(PropertyStatus.Sold, null, ConfirmedVia.Call, null)));
        await host.PublishAsync(Enquiry("cms-2", new() { ["product"] = "REAL_ESTATE", ["propertyId"] = sold.Id }, "lead-2", "contact-2"));
        var second = (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput { LeadId = "lead-2" }))).Items.Single();
        var fromListing = await host.SendAs(CompanyA, new UpdateEnquiryCommand(second.Id, new UpdateEnquiryInput
        {
            Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.NotAvailable, Wanted = "Same flat, it is sold",
        }));
        var r2 = await host.SendAs(CompanyA, new GetRequirementQuery(fromListing.RequirementId!));
        Assert.Equal((PropertyType.Flat, "Sector 71", 2, 7000000m), (Assert.Single(r2.Types), Assert.Single(r2.Localities), Assert.Single(r2.Bhk), r2.BudgetMax!.Value));

        // An enquiry worked into a requirement of its own leaves that requirement to the agent.
        var own = await Requirement("lead-3", "contact-3");
        await host.PublishAsync(Enquiry("cms-3", new() { ["product"] = "REAL_ESTATE" }, "lead-3", "contact-3"));
        var third = (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput { LeadId = "lead-3" }))).Items.Single();
        await host.SendAs(CompanyA, new UpdateEnquiryCommand(third.Id, new UpdateEnquiryInput { RequirementId = own.Id }));
        var kept = await host.SendAs(CompanyA, new UpdateEnquiryCommand(third.Id, new UpdateEnquiryInput
        {
            Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.NotAvailable, Wanted = "Anything in Sector 70",
        }));
        Assert.Equal(own.Id, kept.RequirementId);
        Assert.Equal(RequirementStatus.Active, (await host.SendAs(CompanyA, new GetRequirementQuery(own.Id))).Status);
    }

    [Fact]
    public async Task A_fit_for_a_watched_requirement_is_revived_belled_highlighted_and_never_offered_again_once_shown()
    {
        var watched = await Close((await Requirement()).Id, RequirementStatus.Closed, null, RequirementCloseReason.NotAvailable);
        var plain = await Requirement("lead-2", "contact-2", "Simran", "Sector 71");
        host.Bells.Sent.Clear();

        var flat = await Flat();
        var match = Assert.Single(await Matches(watched.Id));
        Assert.Equal((true, MatchStatus.New), (match.Revived, match.Status));
        var bell = Assert.Single(host.Bells.Sent);
        Assert.Equal(("ESTATE_REVIVED_MATCH", "Rahul Verma asked for this earlier — now available", $"{flat.Code}: {flat.Title}"),
            (bell.Type, bell.Notification.Title, bell.Notification.Body));
        Assert.Equal($"/real-estate/requirements/{watched.Id}", bell.Notification.Url);
        Assert.Contains("contact-1", await Highlighted());
        Assert.True((await host.SendAs(CompanyA, new GetCustomerQuery("contact-1", null))).Highlighted);

        // Revived stays highlighted however old; an ordinary new match only for 7 days.
        await Flat("Sector 71");
        var plainMatch = Assert.Single(await Matches(plain.Id));
        Assert.False(plainMatch.Revived);
        await Change<MatchDocument>(match.Id, m => m.CreatedAt = DateTime.UtcNow.AddDays(-30));
        await Change<MatchDocument>(plainMatch.Id, m => m.CreatedAt = DateTime.UtcNow.AddDays(-8));
        Assert.Equal(["contact-1"], await Highlighted());

        // Shown: no longer highlighted, and a later save of the property offers nothing again (no new match, no bell).
        await host.SendAs(CompanyA, new UpdateMatchCommand(match.Id, new MatchUpdateInput(MatchStatus.Shown, null)));
        Assert.Empty(await Highlighted());
        var other = await Flat(price: 9000000m);
        var second = (await Matches(watched.Id)).Single(m => m.PropertyId == other.Id);
        Assert.True(second.Revived);
        await host.SendAs(CompanyA, new UpdateMatchCommand(second.Id, new MatchUpdateInput(MatchStatus.Rejected, "Too far from school")));
        host.Bells.Sent.Clear();
        await host.SendAs(CompanyA, new ConfirmPropertyCommand(flat.Id, new ConfirmPropertyInput(PropertyStatus.Available, 9400000m, ConfirmedVia.Call, null)));
        Assert.Equal(MatchStatus.Shown, (await Matches(watched.Id)).Single(m => m.PropertyId == flat.Id).Status);
        Assert.Equal(2, (await Matches(watched.Id)).Count);
        Assert.Empty(host.Bells.Sent);

        // Rejected: history too.
        await host.SendAs(CompanyA, new ConfirmPropertyCommand(other.Id, new ConfirmPropertyInput(PropertyStatus.Available, 8800000m, ConfirmedVia.Call, null)));
        Assert.Equal(MatchStatus.Rejected, (await Matches(watched.Id)).Single(m => m.PropertyId == other.Id).Status);
        Assert.Empty(host.Bells.Sent);
    }

    [Fact]
    public async Task The_watch_sweep_closes_ended_watches_and_drops_their_revived_matches()
    {
        var watched = await Close((await Requirement()).Id, RequirementStatus.Closed, "Wants Sector 70 only", RequirementCloseReason.NotAvailable);
        var still = await Close((await Requirement("lead-2", "contact-2", "Simran")).Id, RequirementStatus.Closed, null, RequirementCloseReason.NotAvailable);
        await Flat();
        Assert.Single(await Matches(watched.Id));
        await Change<RequirementDocument>(watched.Id, r => r.WatchUntil = DateTime.UtcNow.AddDays(-1));

        var ended = 0;
        await host.RunAsJobAsync(async sp => ended = await sp.GetRequiredService<WatchedDemand>().EndWatchesAsync(CancellationToken.None));
        Assert.Equal(1, ended);
        var closed = await host.SendAs(CompanyA, new GetRequirementQuery(watched.Id));
        Assert.Equal((RequirementStatus.Closed, RequirementCloseReason.NotAvailable, "Wants Sector 70 only"), (closed.Status, closed.CloseReasonCode, closed.CloseReason));
        Assert.Empty(await Matches(watched.Id));
        Assert.Equal(["contact-2"], await Highlighted());
        Assert.Equal(1, (await host.SendAs(CompanyA, new GetCustomerQuery("contact-1", null))).Numbers.Unmet);   // still unmet, no longer watched

        // A later fit reaches only the one still watched.
        await Flat(price: 9100000m);
        Assert.Empty(await Matches(watched.Id));
        Assert.Equal(2, (await Matches(still.Id)).Count);
    }

    [Fact]
    public async Task A_watched_requirement_is_revived_by_a_project_unit_type_and_the_message_shares_the_project()
    {
        var watched = await host.SendAs(CompanyA, new SaveRequirementCommand(null, new RequirementInput
        {
            LeadId = "lead-1", ContactId = "contact-1", LeadName = "Rahul Verma", LeadPhone = "9811122233", Purpose = PropertyPurpose.Sale,
            Types = [PropertyType.Flat], Localities = ["Sector 82"], Bhk = [3], BudgetMax = 8000000m,
        }));
        await Close(watched.Id, RequirementStatus.Closed, null, RequirementCloseReason.NotAvailable);
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = "Aetro Heights", Kind = ProjectKind.Own, Locality = "Sector 82", Categories = [ProjectCategory.Residential], Stage = ProjectStage.UnderConstruction,
            Reras = [new ReraInput("PBRERA-SAS80-PR0999", "Punjab", "2025-01-10", "2029-12-31", null)],
        }));
        var type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput
        {
            Name = "3 BHK Type A", Type = PropertyType.Flat, Bhk = 3, SuperArea = new AreaInput(1250, AreaUnit.SqFt),
        }));
        host.Bells.Sent.Clear();
        await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput
        {
            ProjectId = project.Id, UnitTypeId = type.Id, Tower = "A", FloorFrom = 1, FloorTo = 3, UnitsPerFloor = 2, Pattern = "{tower}-{floor}{nn}",
        }));

        var match = Assert.Single(await Matches(watched.Id));
        Assert.Equal((project.Id, type.Id, 6, true), (match.ProjectId, match.UnitTypeId, match.AvailableInGroup, match.Revived));
        var bell = Assert.Single(host.Bells.Sent);
        Assert.Equal(("ESTATE_REVIVED_MATCH", "3 BHK Type A in Aetro Heights"), (bell.Type, bell.Notification.Body));
        Assert.Equal(["contact-1"], await Highlighted());

        var sent = await host.SendAs(CompanyA, new SendMatchMessageCommand(match.Id));
        Assert.Equal((false, MatchMessageVia.Draft), (sent.Sent, sent.Via));
        Assert.Contains("/pj/", sent.ShareUrl);
        Assert.Contains("3 BHK Type A in Aetro Heights", sent.Text);
        Assert.Equal(MatchStatus.Shown, Assert.Single(await Matches(watched.Id)).Status);
        Assert.Empty(await Highlighted());
    }

    [Fact]
    public async Task One_click_sends_the_approved_template_through_zeronova_social_and_a_refusal_comes_back_to_the_match()
    {
        await Settings(template: "zeronova_estate_now_available", language: "en");
        var watched = await Close((await Requirement()).Id, RequirementStatus.Closed, null, RequirementCloseReason.NotAvailable);
        var flat = await Flat();
        var match = Assert.Single(await Matches(watched.Id));

        var sent = await host.SendAs(CompanyA, new SendMatchMessageCommand(match.Id));
        Assert.Equal((true, MatchMessageVia.WhatsAppTemplate, null, "919811122233"), (sent.Sent, sent.Via, sent.DraftUrl, sent.Phone));
        var evt = Assert.Single(host.WhatsApp.Sent);
        Assert.Equal((CompanyA, "919811122233", "zeronova_estate_now_available", "en", "Rahul Verma", "lead-1", "user-test"),
            (evt.CompanyId, evt.To, evt.TemplateName, evt.Language, evt.ContactName, evt.LeadId, evt.RequestedBy));
        Assert.Equal(["Rahul Verma", flat.Title, "₹95,00,000", sent.ShareUrl], evt.Parameters!);
        Assert.StartsWith($"re:{CompanyA}:m:{match.Id}:", evt.IdempotencyKey);
        Assert.Null(evt.PhoneNumberId);
        Assert.Contains("/p/", sent.ShareUrl);
        Assert.Equal((MatchStatus.Shown, MatchMessageVia.WhatsAppTemplate, null), (sent.Match.Status, sent.Match.MessageVia, sent.Match.MessageFailed));
        var share = Assert.Single(await host.SendAs(CompanyA, new Application.Features.Visits.GetSharesQuery(flat.Id)));
        Assert.Equal(watched.Id, share.RequirementId);

        // zeronova-social could not send it: kept on the match, the agent told once; another service's failure is not ours.
        host.Bells.Sent.Clear();
        WhatsAppSendFailedNotification Failed(string key) => new()
        {
            CompanyId = CompanyA, IdempotencyKey = key, Reason = "template_not_approved", Error = "Template is not approved.", OccurredAt = DateTime.UtcNow,
            MessageId = Guid.NewGuid().ToString("N"), EventType = "whatsapp-failed", Source = "zeronova-social",
        };
        await host.PublishAsync(Failed($"{CompanyA}:office-run:1b7c2d1e-0000-4000-8000-000000000000"));
        Assert.Empty(host.Bells.Sent);
        await host.PublishAsync(Failed(evt.IdempotencyKey));
        await host.PublishAsync(Failed(evt.IdempotencyKey));
        var failed = Assert.Single(await Matches(watched.Id));
        Assert.Equal(("template_not_approved", MatchStatus.Shown), (failed.MessageFailed, failed.Status));
        var bell = Assert.Single(host.Bells.Sent);
        Assert.Equal(("ESTATE_MESSAGE_FAILED", "WhatsApp to Rahul Verma was not sent"), (bell.Type, bell.Notification.Title));
    }

    [Fact]
    public async Task Without_a_template_one_click_gives_the_wa_me_draft_and_marks_the_match_shown()
    {
        var watched = await Close((await Requirement()).Id, RequirementStatus.Closed, null, RequirementCloseReason.NotAvailable);
        var flat = await Flat();
        var match = Assert.Single(await Matches(watched.Id));

        var draft = await host.SendAs(CompanyA, new SendMatchMessageCommand(match.Id));
        Assert.Equal((false, MatchMessageVia.Draft), (draft.Sent, draft.Via));
        Assert.StartsWith("https://wa.me/919811122233?text=", draft.DraftUrl);
        Assert.Contains(Uri.EscapeDataString(draft.ShareUrl), draft.DraftUrl);
        Assert.StartsWith("Namaste Rahul Verma, you had asked us for a property like this — it is available now: " + flat.Title, draft.Text);
        Assert.Empty(host.WhatsApp.Sent);
        Assert.Equal((MatchStatus.Shown, MatchMessageVia.Draft), (draft.Match.Status, draft.Match.MessageVia));

        // Another agent's match is refused; a rejected one is never offered again.
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-x", [Models.Constants.EstatePermissions.RequirementManage],
            new SendMatchMessageCommand(match.Id)));
        await host.SendAs(CompanyA, new UpdateMatchCommand(match.Id, new MatchUpdateInput(MatchStatus.Rejected, "Not this one")));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SendMatchMessageCommand(match.Id)));
    }

    [Fact]
    public async Task The_demand_report_groups_unmet_demand_and_counts_empty_website_searches_without_who_searched()
    {
        // (a) watched; (b) active with no match for days; (d) an enquiry closed not available that made no watched requirement.
        await Close((await Requirement("lead-1", "contact-1", "Rahul Verma")).Id, RequirementStatus.Closed, null, RequirementCloseReason.NotAvailable);
        var unmatched = await Requirement("lead-2", "contact-2", "Simran", budgetMax: 7000000m);
        await Change<RequirementDocument>(unmatched.Id, r => r.CreatedBy!.ChangedDate = DateTimeOffset.UtcNow.AddDays(-20));
        var fresh = await Requirement("lead-3", "contact-3", "Aman");   // too new to count as unmatched
        await host.PublishAsync(Enquiry("cms-9", new() { ["product"] = "REAL_ESTATE" }, "lead-3", "contact-3", "Aman"));
        var enquiry = (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput { LeadId = "lead-3" }))).Items.Single();
        await host.SendAs(CompanyA, new UpdateEnquiryCommand(enquiry.Id, new UpdateEnquiryInput { RequirementId = fresh.Id }));
        await host.SendAs(CompanyA, new UpdateEnquiryCommand(enquiry.Id, new UpdateEnquiryInput
        {
            Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.NotAvailable, Wanted = "3 BHK flat in Sector 70 under 1 cr",
        }));

        // (e) the website: two searches for 3 BHK flats in Sector 70 found nothing (by slug and by name), one in Sector 82, one with text only.
        var code = (await host.SendAs(CompanyA, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(true, null)))).Code;
        var sector70 = new EstateWebsitePropertyFilterInput { Purpose = PropertyPurpose.Sale, Types = [PropertyType.Flat], Locality = "sector-70", Bhk = [3], PriceTo = 10000000m };
        Assert.Equal(0, (await Public(new GetEstateWebsitePropertiesQuery(code, sector70, EstateWebsitePropertySort.Featured, 1, 12))).TotalCount);
        await Public(new GetEstateWebsitePropertiesQuery(code, new EstateWebsitePropertyFilterInput
        {
            Purpose = PropertyPurpose.Sale, Types = [PropertyType.Flat], Locality = "Sector 70", Bhk = [3], PriceTo = 10000000m,
        }, EstateWebsitePropertySort.Newest, 1, 12));
        await Public(new GetEstateWebsitePropertiesQuery(code, new EstateWebsitePropertyFilterInput { Locality = "Sector 82" }, EstateWebsitePropertySort.Featured, 1, 12));
        await Public(new GetEstateWebsitePropertiesQuery(code, new EstateWebsitePropertyFilterInput { Text = "rahul 9811122233" }, EstateWebsitePropertySort.Featured, 1, 12));
        await Public(new GetEstateWebsitePropertiesQuery(code, sector70, EstateWebsitePropertySort.Featured, 2, 12));   // a later page is not counted

        // Kept per day and criteria with a count, and nothing about who searched.
        List<BsonDocument> rows = [];
        await host.WithAsync(CompanyA, async sp => rows = await sp.GetRequiredService<IMongoDatabase>().GetCollection<BsonDocument>("re_demand_signals")
            .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync());
        Assert.Equal(2, rows.Count);
        Assert.Equal([1, 2], rows.Select(r => r["count"].AsInt32).Order());
        var allowed = new HashSet<string>
        {
            "_id", "companyId", "day", "key", "purpose", "types", "city", "locality", "localityKey", "budgetBand", "bhk", "count", "isDeleted", "version",
            "createdBy", "updatedBy",
        };
        foreach (var row in rows)
            Assert.All(row.Names, name => Assert.Contains(name, allowed));
        Assert.DoesNotContain(rows, r => r.ToJson().Contains("9811122233") || r.ToJson().Contains("rahul", StringComparison.OrdinalIgnoreCase));
        var flats = rows.Single(r => r["count"].AsInt32 == 2);
        Assert.Equal(("sector 70", "50L-1Cr"), (flats["localityKey"].AsString, flats["budgetBand"].AsString));

        // By locality: Sector 70 holds every source; Sector 82 only the search (with no customer behind it).
        var report = await host.SendAs(CompanyA, new GetDemandReportQuery(null, null, [DemandGroupBy.Locality], 14));
        Assert.Equal([(DemandSource.Watching, 1), (DemandSource.Unmatched, 1), (DemandSource.EnquiryNotAvailable, 1), (DemandSource.WebsiteSearch, 3)],
            report.Totals.Select(t => (t.Source, t.Count)));
        var s70 = report.Groups.Single(g => g.Locality == "Sector 70" && g.Purpose == PropertyPurpose.Sale);
        Assert.Equal(5, s70.Total);
        Assert.Equal([(DemandSource.Watching, 1), (DemandSource.Unmatched, 1), (DemandSource.EnquiryNotAvailable, 1), (DemandSource.WebsiteSearch, 2)],
            s70.BySource.Select(b => (b.Source, b.Count)));
        Assert.Equal(["Aman", "Rahul Verma", "Simran"], s70.Samples.Select(x => x.Name).Order());
        Assert.Equal((null, null, null), (s70.Type, s70.Bhk, s70.BudgetBand));
        var s82 = report.Groups.Single(g => g.Locality == "Sector 82");
        Assert.Equal((null, 1, 0), (s82.Purpose, s82.Total, s82.Samples.Count));

        // By every dimension: the budget bands and BHK split the groups.
        var full = await host.SendAs(CompanyA, new GetDemandReportQuery(null, null, null, 14));
        Assert.Equal([DemandGroupBy.Locality, DemandGroupBy.Type, DemandGroupBy.Bhk, DemandGroupBy.BudgetBand], full.GroupBy);
        var top = full.Groups[0];
        Assert.Equal(("Sector 70", "Flat", "3", "50L-1Cr"), (top.Locality, top.Type, top.Bhk, top.BudgetBand));
        Assert.Contains(full.Groups, g => g.BudgetBand == "50L-1Cr" && g.Samples.Any(x => x.Name == "Simran"));
        Assert.Contains(full.Groups, g => g is { Locality: "Sector 82", Type: "Any", Bhk: "Any", BudgetBand: "Any" });

        // An agent sees only their own customers.
        var mine = await host.SendAsMember(CompanyA, "agent-z", [Models.Constants.EstatePermissions.RequirementManage],
            new GetDemandReportQuery(null, null, [DemandGroupBy.Locality], 14));
        Assert.Empty(mine.Groups.SelectMany(g => g.Samples));
    }

    [Fact]
    public async Task The_projects_list_counts_each_projects_enquiries()
    {
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = "Green Valley", Kind = ProjectKind.Partner, Locality = "Sector 115", BuilderName = "GV Builders", Stage = ProjectStage.NewLaunch,
            Categories = [ProjectCategory.Residential],
        }));
        var quiet = await host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = "Quiet Towers", Kind = ProjectKind.Partner, Locality = "Sector 116", BuilderName = "QT", Stage = ProjectStage.NewLaunch,
            Categories = [ProjectCategory.Residential],
        }));
        await host.PublishAsync(Enquiry("cms-1", new() { ["product"] = "REAL_ESTATE", ["projectId"] = project.Id }));
        await host.PublishAsync(Enquiry("cms-2", new() { ["product"] = "REAL_ESTATE", ["projectId"] = project.Id }, "lead-2", "contact-2"));

        var list = await host.SendAs(CompanyA, new GetProjectsQuery(new ProjectSearchInput()));
        var counted = list.Single(p => p.Id == project.Id).Enquiries;
        Assert.Equal((2, 2, 2), (counted.Total, counted.Last7Days, counted.Last30Days));
        Assert.Equal(EnquirySource.WebsiteForm, Assert.Single(counted.BySource).Source);
        Assert.Equal(0, list.Single(p => p.Id == quiet.Id).Enquiries.Total);
    }
}
