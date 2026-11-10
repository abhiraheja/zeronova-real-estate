using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Api.Graphql;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Brokerage;
using ZeroNovaRealEstate.Application.Features.Customers;
using ZeroNovaRealEstate.Application.Features.Deals;
using ZeroNovaRealEstate.Application.Features.Enquiries;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Features.Parties;
using ZeroNovaRealEstate.Application.Features.Projects;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Rentals;
using ZeroNovaRealEstate.Application.Features.Requirements;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Features.Videos;
using ZeroNovaRealEstate.Application.Features.Website;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Configuration;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Money;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Settings;
using ZeroNovaRealEstate.Models.Videos;
using ZeroNovaRealEstate.Models.Website;
using static ZeroNovaRealEstate.Tests.TestHost;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// The review fixes of 2026-11-11, one test (or more) per finding: own-capture metadata, bounded demand signals, budget
/// parsing, the C5 bells' cut-off, the one-click message's throttle and freshness, dashboard scoping, scheduled price
/// lists, hold exposure, share rights, re-matching only what changed, the video and enquiry-code races, the watch sweep,
/// CP terms and the settings for PPJM, the brokerage's typed TDS and clearing a party's residency or gender.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class ReviewFixTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        await Settings();
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private static string Today => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Shift(int days) =>
        DateOnly.ParseExact(Today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Task<EstateSettingsDto> Settings(string? template = null) =>
        host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali",
            "PBRERA-SAS79-REA0123", "Sharma Properties", "9876012345", MatchTemplateName: template, MatchTemplateLanguage: template is null ? null : "en")));

    private Task<RequirementDto> Requirement(string locality = "Sector 70", string lead = "lead-1", string contact = "contact-1", string? agent = null,
        string name = "Rahul Verma", string phone = "9811122233") =>
        host.SendAs(CompanyA, new SaveRequirementCommand(null, new RequirementInput
        {
            LeadId = lead, ContactId = contact, LeadName = name, LeadPhone = phone, AgentId = agent, Purpose = PropertyPurpose.Sale, Types = [PropertyType.Flat],
            Localities = [locality], Bhk = [3], BudgetMin = 6000000m, BudgetMax = 10000000m,
        }));

    private Task<PropertyDto> Flat(string locality = "Sector 70", decimal price = 9500000m) =>
        host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = locality, Bhk = 3, SuperArea = new AreaInput(1650, AreaUnit.SqFt),
            Price = price,
        }));

    private Task<IReadOnlyList<MatchDto>> Matches(string requirementId) => host.SendAs(CompanyA, new GetMatchesQuery(null, requirementId, null));

    private Task<T> Public<T>(IRequest<T> request)
    {
        T result = default!;
        return host.RunAsJobAsync(async sp => result = await sp.GetRequiredService<IMediator>().Send(request)).ContinueWith(t =>
        {
            t.GetAwaiter().GetResult();
            return result;
        });
    }

    private IMongoCollection<T> Collection<T>(IServiceProvider sp, string name) => sp.GetRequiredService<IMongoDatabase>().GetCollection<T>(name);

    /// <summary>Changes a stored row directly (to stand for an old row, or to move its dates).</summary>
    private Task Change<T>(string collection, string id, Action<T> change) where T : Domain.Base.CompanyDocumentBase =>
        host.WithAsync(CompanyA, async sp =>
        {
            var rows = Collection<T>(sp, collection);
            var row = await rows.Find(x => x.Id == id).FirstAsync();
            change(row);
            await rows.ReplaceOneAsync(x => x.Id == id, row);
        });

    private static LeadEnquiryReceivedNotification Received(string externalRef, Dictionary<string, string> metadata, string lead = "lead-1", string? assigned = "user-test",
        string? contact = null, string name = "Rahul Verma", string phone = "+919811122233") =>
        new()
        {
            CompanyId = CompanyA, LeadId = lead, ContactId = contact ?? "contact-" + lead, Name = name, Phone = phone, OccurredAt = DateTimeOffset.UtcNow,
            Channel = "website", ActivityChannel = "Website", ExternalRef = externalRef, ActivityId = "act-" + externalRef, AssignedTo = assigned,
            AssignedToName = assigned is null ? null : "Asha", Metadata = metadata, IsNewLead = true, MessageId = Guid.NewGuid().ToString("N"),
            EventType = "lead-enquiry-received", Source = "zeronova-lead", ChangedDate = DateTimeOffset.UtcNow,
        };

    private async Task<IReadOnlyList<EnquiryDto>> Enquiries() => (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput()))).Items;

    // ── 1. Own-capture metadata ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_website_forms_extra_fields_cannot_claim_our_own_capture_its_agent_or_source()
    {
        // A website form's extra fields try to name the agent and the source: ignored, and not shown as extras.
        await host.PublishAsync(Received("cms-1", new()
        {
            ["product"] = "REAL_ESTATE", ["form"] = "requirement", ["budget"] = "80 lakh", ["estate"] = "1", ["agentId"] = "agent-evil", ["agentName"] = "Evil",
            ["estateSource"] = "WalkIn",
        }));
        var forged = Assert.Single(await Enquiries());
        Assert.Equal(("user-test", "Asha", EnquirySource.WebsiteForm), (forged.AgentId, forged.AgentName, forged.Source));
        Assert.Equal(["budget"], forged.Extra.Select(x => x.Key));

        // "estate = 1" alone no longer makes a generic form Real Estate's.
        await host.PublishAsync(Received("cms-2", new() { ["form"] = "contact", ["estate"] = "1" }, "lead-2"));
        Assert.Single(await Enquiries());

        // The AI office (office: refs): estate and estateSource = AiOffice are trusted, its agentId never.
        await host.PublishAsync(Received("office:conv-1:P-0001:2026-11-10", new()
        {
            ["estate"] = "1", ["estateSource"] = "AiOffice", ["form"] = "ai-office", ["agentId"] = "agent-evil", ["wanted"] = "3 BHK in Sector 70",
        }, "lead-3"));
        var office = (await Enquiries()).Single(e => e.LeadId == "lead-3");
        Assert.Equal((EnquirySource.AiOffice, "user-test"), (office.Source, office.AgentId));
        Assert.DoesNotContain(office.Extra, x => x.Key is "agentId" or "estate" or "estateSource");

        // An office ref claiming another source is read by its channel.
        await host.PublishAsync(Received("office:conv-2:x:1", new() { ["estate"] = "1", ["estateSource"] = "WalkIn" }, "lead-4"));
        Assert.Equal(EnquirySource.WebsiteForm, (await Enquiries()).Single(e => e.LeadId == "lead-4").Source);

        // Our own capture (estate: refs) is trusted as before.
        await host.PublishAsync(Received(EnquiryRefs.New(), new() { ["estate"] = "1", ["estateSource"] = "WalkIn", ["agentId"] = "agent-7" }, "lead-5"));
        var own = (await Enquiries()).Single(e => e.LeadId == "lead-5");
        Assert.Equal((EnquirySource.WalkIn, "agent-7"), (own.Source, own.AgentId));
    }

    // ── 11b. Enquiry codes: a duplicate never takes a number ────────────────────────────────────────────────

    [Fact]
    public async Task Parallel_deliveries_of_one_enquiry_keep_one_row_and_consume_one_code()
    {
        var once = Received("cms-dup", new() { ["product"] = "REAL_ESTATE" });
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => host.PublishAsync(once)));
        await host.PublishAsync(once);   // a redelivery later
        Assert.Equal("E-0001", Assert.Single(await Enquiries()).Code);

        await host.PublishAsync(Received("cms-next", new() { ["product"] = "REAL_ESTATE" }, "lead-2"));
        Assert.Equal(["E-0001", "E-0002"], (await Enquiries()).Select(e => e.Code).Order());
    }

    // ── 2. Demand signals are bounded ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Empty_searches_keep_only_known_places_short_names_and_at_most_500_criteria_a_day()
    {
        await host.WithAsync(CompanyA, sp => sp.GetRequiredService<ILocalityRepository>().EnsureAsync("Sector 70", "sector 70", "Mohali"));
        var code = (await host.SendAs(CompanyA, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(true, null)))).Code;
        Task Search(EstateWebsitePropertyFilterInput f) => Public(new GetEstateWebsitePropertiesQuery(code, f, EstateWebsitePropertySort.Featured, 1, 12));

        await Search(new() { Purpose = PropertyPurpose.Sale, City = "mohali", Locality = "sector-70" });           // known: kept as the list spells it
        await Search(new() { Purpose = PropertyPurpose.Sale, City = "Ludhiana", Locality = "Haxor Street 9" });    // unknown: "*"
        await Search(new() { Purpose = PropertyPurpose.Rent, Locality = new string('x', 500) });                     // too long: "*"
        List<DemandSignalDocument> rows = [];
        await host.WithAsync(CompanyA, async sp => rows = await Collection<DemandSignalDocument>(sp, "re_demand_signals").Find(_ => true).ToListAsync());
        Assert.Equal(3, rows.Count);
        var known = rows.Single(r => r.LocalityKey is not null);
        Assert.Equal(("Mohali", "Sector 70", "sector 70"), (known.City, known.Locality, known.LocalityKey));
        Assert.All(rows.Where(r => r != known), r => Assert.True(r.City is null && r.Locality is null && r.LocalityKey is null));
        Assert.DoesNotContain(rows, r => r.Key.Contains("haxor") || r.Key.Contains("xxx") || r.Key.Contains("ludhiana"));

        // A day full of criteria: new ones are counted on the "other" row, an existing one still counts on its own.
        var day = rows[0].Day;
        await host.WithAsync(CompanyA, sp => Collection<DemandSignalDocument>(sp, "re_demand_signals").InsertManyAsync(Enumerable.Range(0, 500)
            .Select(i => new DemandSignalDocument { Id = ObjectId.GenerateNewId().ToString(), CompanyId = CompanyA, Day = day, Key = $"filler-{i}", Count = 1 })));
        await Search(new() { Purpose = PropertyPurpose.Rent, Bhk = [2] });
        await Search(new() { Purpose = PropertyPurpose.Sale, Bhk = [4] });
        await Search(new() { Purpose = PropertyPurpose.Sale, City = "mohali", Locality = "sector-70" });
        await host.WithAsync(CompanyA, async sp => rows = await Collection<DemandSignalDocument>(sp, "re_demand_signals").Find(_ => true).ToListAsync());
        Assert.Equal(504, rows.Count);
        var other = rows.Single(r => r.Key == DemandSignals.OverflowKey);
        Assert.Equal((2, (PropertyPurpose?)null, 0), (other.Count, other.Purpose, other.Bhk.Count));
        Assert.Equal(2, rows.Single(r => r.LocalityKey == "sector 70").Count);

        // The report reads them (bounded) and counts every search.
        var report = await host.SendAs(CompanyA, new GetDemandReportQuery(null, null, [DemandGroupBy.Locality]));
        Assert.Equal(3 + 500 + 3, report.Totals.Single(t => t.Source == DemandSource.WebsiteSearch).Count);
    }

    // ── 3. Budgets out of free text ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("80 lakh", false, new[] { 8000000.0 })]
    [InlineData("50-80 lakh", false, new[] { 5000000.0, 8000000.0 })]
    [InlineData("1.2 cr", false, new[] { 12000000.0 })]
    [InlineData("25k a month", false, new[] { 25000.0 })]
    [InlineData("Rs 25000", false, new[] { 25000.0 })]
    [InlineData("₹ 25,000", false, new[] { 25000.0 })]
    [InlineData("INR 30000", false, new[] { 30000.0 })]
    [InlineData("₹80,00,000", false, new[] { 8000000.0 })]
    [InlineData("budget 150000", false, new[] { 150000.0 })]
    [InlineData("25000", false, new double[0])]
    [InlineData("25000", true, new[] { 25000.0 })]
    [InlineData("1650 sq ft flat", false, new double[0])]
    [InlineData("1650 sqft", true, new double[0])]
    [InlineData("200 sq. yd plot", false, new double[0])]
    [InlineData("1200-1500 sq ft", true, new double[0])]
    [InlineData("200 gaj", true, new double[0])]
    [InlineData("10 marla kothi", false, new double[0])]
    [InlineData("2 kanal", true, new double[0])]
    [InlineData("1 acre farm", false, new double[0])]
    [InlineData("3 bigha", true, new double[0])]
    [InlineData("built in 2015", false, new double[0])]
    [InlineData("2026", true, new double[0])]
    [InlineData("call 9811122233", false, new double[0])]
    [InlineData("9811122233", true, new double[0])]
    [InlineData("3 BHK in Sector 70, 1650 sq ft, under 90 lakh, possession 2027", false, new[] { 9000000.0 })]
    public void Amounts_skip_areas_years_phones_and_small_bare_numbers(string text, bool moneyField, double[] expected) =>
        Assert.Equal(expected.Select(x => (decimal)x), DemandText.Amounts(text, moneyField));

    [Fact]
    public async Task A_size_field_is_not_a_place_and_free_text_sizes_years_and_phones_are_no_budget()
    {
        await host.PublishAsync(Received("cms-1", new()
        {
            ["product"] = "REAL_ESTATE", ["form"] = "requirement", ["plotArea"] = "250 sq yd", ["superArea"] = "1650", ["area"] = "Sector 71",
        }));
        var enquiry = Assert.Single(await Enquiries());
        var closed = await host.SendAs(CompanyA, new UpdateEnquiryCommand(enquiry.Id, new UpdateEnquiryInput
        {
            Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.NotAvailable, Wanted = "3 BHK flat, 1650 sq ft, possession 2027, call 9811122233",
        }));
        var watched = await host.SendAs(CompanyA, new GetRequirementQuery(closed.RequirementId!));
        Assert.Equal(["Sector 71"], watched.Localities);
        Assert.Equal(((decimal?)null, (decimal?)null), (watched.BudgetMin, watched.BudgetMax));
    }

    // ── 4. The C5 bells: only tenancies of C5's time; not Chandigarh is not read again ────────────────────────

    [Fact]
    public async Task Tenancies_from_before_c5_do_not_ring_and_a_non_chandigarh_one_is_marked_not_applicable()
    {
        Assert.Equal("2026-11-11", new RentalOptions().ComplianceBellsFrom);
        var landlord = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "Harpreet Singh", Phone = "9814000001" }));
        var tenant = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "Anjali Sharma", Phone = "9814000002" }));
        async Task<TenancyDto> Tenancy(string start, string? state = null, string? agreement = null)
        {
            var p = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
            {
                Purpose = PropertyPurpose.Rent, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 70", State = state, Bhk = 2, Rent = 25000m,
            }));
            await host.SendAs(CompanyA, new SetPropertyOwnersCommand(p.Id, [new PropertyOwnerInput(landlord.Id, null, HoldingKind.RegisteredSaleDeed)]));
            return await host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
            {
                PropertyId = p.Id, TenantIds = [tenant.Id], Rent = 25000m, StartDate = start, AgreementDate = agreement, RentDueDay = 28,
            }));
        }
        Task MakeOld(string id) => Change<TenancyDocument>("re_tenancies", id, t => t.CreatedBy!.ChangedDate = new DateTimeOffset(2025, 12, 1, 6, 0, 0, TimeSpan.Zero));

        // Before the cut-off (2026-01-01 here): started and made then — no police bell, no Rent Authority bell.
        var oldPunjab = await Tenancy("2025-12-01");
        var oldChandigarh = await Tenancy("2025-12-01", "Chandigarh", "2025-12-01");
        await MakeOld(oldPunjab.Id);
        await MakeOld(oldChandigarh.Id);
        // Of C5's time: one already rung before the fix (kept so), one not yet.
        var rung = await Tenancy(Shift(-10));
        await Change<TenancyDocument>("re_tenancies", rung.Id, t => t.PoliceReminded = true);
        var fresh = await Tenancy(Shift(-50));   // Punjab, the intimation's window open: read once, marked not applicable

        host.Bells.Sent.Clear();
        await host.RunAsJobAsync(sp => sp.GetRequiredService<RentReminders>().RunOnceAsync(CancellationToken.None));
        await host.RunAsJobAsync(sp => sp.GetRequiredService<RentReminders>().RunOnceAsync(CancellationToken.None));
        var police = host.Bells.Sent.Where(b => b.Type == "ESTATE_POLICE_VERIFICATION").ToList();
        Assert.Equal([fresh.Id], police.Select(b => b.Meta.CorrelationId!.Split(':')[0]));
        Assert.DoesNotContain(host.Bells.Sent, b => b.Type == "ESTATE_RENT_AUTHORITY_DUE");

        TenancyDocument row = null!;
        await host.WithAsync(CompanyA, async sp => row = await Collection<TenancyDocument>(sp, "re_tenancies").Find(t => t.Id == fresh.Id).FirstAsync());
        Assert.True(row.RentAuthorityNotApplicable);
        await host.WithAsync(CompanyA, async sp => row = await Collection<TenancyDocument>(sp, "re_tenancies").Find(t => t.Id == oldChandigarh.Id).FirstAsync());
        Assert.False(row.RentAuthorityReminded || row.PoliceReminded);   // never rung, still shown on screen
        Assert.True((await host.SendAs(CompanyA, new GetTenancyQuery(oldChandigarh.Id))).Compliance.RentAuthorityOverdue);

        Assert.True(RentReminders.OfC5(new TenancyDocument { Id = ObjectId.GenerateNewId().ToString(), PropertyId = "p", StartDate = "2026-11-11", EndDate = "2027-10-10", AgentId = "a" },
            RentalOptions.C5Release));
    }

    // ── 5. One-click message: throttle, stable key, freshness ─────────────────────────────────────────────────

    [Fact]
    public async Task A_second_whatsapp_send_within_ten_minutes_is_refused_and_a_stale_listing_is_never_sent()
    {
        await Settings(template: "zeronova_estate_now_available");
        var requirement = await Requirement();
        var flat = await Flat();
        var match = Assert.Single(await Matches(requirement.Id));

        var sent = await host.SendAs(CompanyA, new SendMatchMessageCommand(match.Id));
        Assert.True(sent.Sent);
        var key = Assert.Single(host.WhatsApp.Sent).IdempotencyKey;
        var slot = DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(MatchMessages.ResendMinutes).Ticks;
        Assert.Contains(key, new[] { $"re:{CompanyA}:m:{match.Id}:{slot}", $"re:{CompanyA}:m:{match.Id}:{slot - 1}" });
        var again = await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SendMatchMessageCommand(match.Id)));
        Assert.Contains("Wait 10 minutes", again.Message);
        Assert.Single(host.WhatsApp.Sent);

        // Eleven minutes later it may go again.
        await Change<MatchDocument>("re_matches", match.Id, m => m.MessagedAt = DateTime.UtcNow.AddMinutes(-11));
        Assert.True((await host.SendAs(CompanyA, new SendMatchMessageCommand(match.Id))).Sent);

        // A stale property is not sent (D6).
        await Change<MatchDocument>("re_matches", match.Id, m => m.MessagedAt = DateTime.UtcNow.AddHours(-1));
        await Change<PropertyDocument>("re_properties", flat.Id, p => p.LastConfirmedAt = DateTime.UtcNow.AddDays(-400));
        var stale = await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SendMatchMessageCommand(match.Id)));
        Assert.Contains("not confirmed recently", stale.Message);
    }

    [Fact]
    public async Task A_unit_type_match_of_a_partner_project_not_confirmed_with_the_builder_is_not_sent()
    {
        var requirement = await Requirement("Sector 115");
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = "Green Valley", Kind = ProjectKind.Partner, Locality = "Sector 115", BuilderName = "GV Builders", Stage = ProjectStage.NewLaunch,
            Categories = [ProjectCategory.Residential],
        }));
        var type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput
        {
            Name = "3 BHK", Type = PropertyType.Flat, Bhk = 3, SuperArea = new AreaInput(1500, AreaUnit.SqFt),
        }));
        await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput
        {
            ProjectId = project.Id, UnitTypeId = type.Id, Tower = "A", FloorFrom = 1, FloorTo = 2, UnitsPerFloor = 1, Pattern = "{tower}-{floor}{nn}",
        }));
        var match = Assert.Single(await Matches(requirement.Id));
        await Change<ProjectDocument>("re_projects", project.Id, p => p.LastConfirmedAt = DateTime.UtcNow.AddDays(-400));
        var refused = await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SendMatchMessageCommand(match.Id)));
        Assert.Contains("not confirmed with the builder", refused.Message);
    }

    // ── 6. The customer's dashboard without PRQV ────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_agent_sees_the_customer_from_their_own_rows_and_a_stranger_gets_a_blank_answer()
    {
        await Requirement(agent: "agent-x", name: "Sunita", phone: "9833344455");
        await host.PublishAsync(Received("cms-1", new() { ["product"] = "REAL_ESTATE" }, "lead-1", "agent-z", "contact-1", "S. Rani (website)", "+919800000001"));

        var dealer = await host.SendAs(CompanyA, new GetCustomerQuery("contact-1", null));
        Assert.Equal("S. Rani (website)", dealer.Name);   // the latest touch of all

        var agent = await host.SendAsMember(CompanyA, "agent-x", [P.RequirementManage], new GetCustomerQuery("contact-1", null));
        Assert.Equal(("Sunita", "919833344455"), (agent.Name, agent.Phone));
        Assert.Empty(agent.Enquiries);

        var stranger = await host.SendAsMember(CompanyA, "agent-y", [P.RequirementManage], new GetCustomerQuery("contact-1", null));
        Assert.Equal(((string?)null, (string?)null, 0), (stranger.Name, stranger.Phone, stranger.LeadIds.Count));
        Assert.Empty(stranger.Requirements);
        Assert.Empty(stranger.Enquiries);
        Assert.Null(stranger.LastActivityAt);
    }

    // ── 7. Future-dated price lists ──────────────────────────────────────────────────────────────────────────

    private async Task<(ProjectDto Project, UnitTypeDto Type)> OwnProject(string name = "Aetro Heights", IReadOnlyList<PhotoInput>? photos = null)
    {
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, OwnInput(name, photos)));
        var type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput
        {
            Name = "3 BHK Type A", Type = PropertyType.Flat, Bhk = 3, SuperArea = new AreaInput(1250, AreaUnit.SqFt),
        }));
        await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput
        {
            ProjectId = project.Id, UnitTypeId = type.Id, Tower = "A", FloorFrom = 1, FloorTo = 2, UnitsPerFloor = 1, Pattern = "{tower}-{floor}{nn}",
        }));
        return (project, type);
    }

    private static ProjectInput OwnInput(string name = "Aetro Heights", IReadOnlyList<PhotoInput>? photos = null) => new()
    {
        Name = name, Kind = ProjectKind.Own, Locality = "Sector 82", City = "Mohali", Categories = [ProjectCategory.Residential], Stage = ProjectStage.UnderConstruction,
        GstPercent = 5m, Towers = [new TowerInput("A", 12)], Reras = [new ReraInput("PBRERA-SAS80-PR0999", "Punjab", "2025-01-10", "2029-12-31", null)],
        Photos = photos,
    };

    [Fact]
    public async Task A_price_list_dated_later_is_scheduled_and_prices_the_units_when_its_day_comes()
    {
        var (project, type) = await OwnProject();
        await host.SendAs(CompanyA, new SavePriceListCommand(project.Id, new PriceListInput([new UnitTypeRateInput(type.Id, RateBasis.PerSqFt, 4000m)], null, Today, null)));
        var v2 = await host.SendAs(CompanyA, new SavePriceListCommand(project.Id,
            new PriceListInput([new UnitTypeRateInput(type.Id, RateBasis.PerSqFt, 5000m)], null, Shift(5), null)));
        Assert.True(v2.Scheduled);

        async Task<decimal?[]> Prices() => [.. (await host.SendAs(CompanyA, new GetProjectUnitsQuery(project.Id, null, null, null))).Select(u => u.Price)];
        Assert.All(await Prices(), p => Assert.Equal(5_250_000m, p));   // 1250 × 4000 + 5% GST: still v1
        var shown = await host.SendAs(CompanyA, new GetProjectQuery(project.Id));
        Assert.Equal((1, false, 2, true), (shown.PriceList!.Version, shown.PriceList.Scheduled, shown.ScheduledPriceList!.Version, shown.ScheduledPriceList.Scheduled));
        Assert.Equal([true, false], (await host.SendAs(CompanyA, new GetPriceListVersionsQuery(project.Id))).Select(v => v.Scheduled));
        var unit = (await host.SendAs(CompanyA, new GetProjectUnitsQuery(project.Id, null, null, null)))[0];
        Assert.Equal(1, (await host.SendAs(CompanyA, new GetUnitCostSheetQuery(unit.Id, null))).PriceListVersion);

        // Its day comes: the step (with the hold sweep) prices the units from it, once.
        var applied = 0;
        await host.RunAsJobAsync(async sp => applied = await sp.GetRequiredService<ScheduledPriceListStep>().RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, applied);
        await Change<PriceListDocument>("re_price_lists", v2.Id, l => l.EffectiveFrom = Today);
        await host.RunAsJobAsync(async sp => applied = await sp.GetRequiredService<ScheduledPriceListStep>().RunOnceAsync(CancellationToken.None));
        Assert.Equal(1, applied);
        Assert.All(await Prices(), p => Assert.Equal(6_562_500m, p));   // 1250 × 5000 + 5% GST
        await host.RunAsJobAsync(async sp => applied = await sp.GetRequiredService<ScheduledPriceListStep>().RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, applied);
        shown = await host.SendAs(CompanyA, new GetProjectQuery(project.Id));
        Assert.Equal((2, (PriceListDto?)null), (shown.PriceList!.Version, shown.ScheduledPriceList));
    }

    // ── 8. Whom a unit is held for ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Whom_a_unit_is_held_for_is_shown_only_to_the_member_who_held_it_and_prqv()
    {
        var (project, _) = await OwnProject();
        var units = await host.SendAs(CompanyA, new GetProjectUnitsQuery(project.Id, null, null, null));
        string[] agent = [P.ProjectManage, P.PropertyView, P.RequirementManage];
        var held = await host.SendAsMember(CompanyA, "agent-x", agent, new SetUnitStatusCommand(units[0].Id,
            new UnitStatusInput(UnitStatus.Blocked, Shift(3), "lead-7", "Simran")));
        Assert.Equal("lead-7", held.HoldLeadId);

        async Task<UnitDto> As(string user, string[] permissions) =>
            (await host.SendAsMember(CompanyA, user, permissions, new GetProjectUnitsQuery(project.Id, null, null, null))).Single(u => u.Id == units[0].Id);
        Assert.Equal(("lead-7", "Simran"), ((await As("agent-x", agent)).HoldLeadId, (await As("agent-x", agent)).HoldLeadName));
        Assert.Equal(((string?)null, (string?)null), ((await As("agent-y", agent)).HoldLeadId, (await As("agent-y", agent)).HoldLeadName));
        Assert.Equal("lead-7", (await As("manager", [.. agent, P.RequirementView])).HoldLeadId);
        Assert.Equal(UnitStatus.Blocked, (await As("agent-y", agent)).Status);   // the hold itself is no secret
        Assert.Null((await host.SendAsMember(CompanyA, "agent-y", agent, new GetPropertyQuery(units[0].Id))).HoldLeadId);
        Assert.Equal("lead-7", (await host.SendAs(CompanyA, new GetPropertyQuery(units[0].Id))).HoldLeadId);
    }

    // ── 9 & 14. Resolver permissions ────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> Codes(Type resolvers, string method) =>
        [.. resolvers.GetMethod(method)!.GetCustomAttributesData().Single(a => a.AttributeType.Name == "RequirePermissionAttribute").ConstructorArguments
            .SelectMany(a => (IEnumerable<CustomAttributeTypedArgument>)a.Value!).Select(a => (string)a.Value!)];

    [Fact]
    public void Whoever_may_send_a_match_message_may_share_and_ppjm_reads_the_settings_and_states()
    {
        var send = Codes(typeof(EstateMutation), nameof(EstateMutation.SendMatchMessage));
        Assert.All(send, code => Assert.Contains(code, Codes(typeof(VisitMutation), nameof(VisitMutation.ShareProperty))));
        Assert.All(send, code => Assert.Contains(code, Codes(typeof(ProjectMutation), nameof(ProjectMutation.ShareProject))));
        Assert.Contains(P.ProjectManage, Codes(typeof(EstateQuery), nameof(EstateQuery.Settings)));
        Assert.Contains(P.ProjectManage, Codes(typeof(EstateQuery), nameof(EstateQuery.States)));
    }

    // ── 10. A project save re-matches only what changed ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_project_save_re_matches_only_unit_types_whose_units_changed_for_matching()
    {
        var requirement = await Requirement("Sector 82");
        var (project, _) = await OwnProject();
        var match = Assert.Single(await Matches(requirement.Id));
        await host.WithAsync(CompanyA, sp => Collection<MatchDocument>(sp, "re_matches").DeleteOneAsync(m => m.Id == match.Id));

        // New photos only: the units change, but nothing a customer is matched on — no re-match (the dropped match stays gone).
        await host.SendAs(CompanyA, new SaveProjectCommand(project.Id, OwnInput(photos: [new PhotoInput("p/2.jpg", "https://cdn.test/2.jpg")])));
        Assert.Empty(await Matches(requirement.Id));

        // A new name is on the matches (the society): matched again.
        await host.SendAs(CompanyA, new SaveProjectCommand(project.Id, OwnInput("Aetro Heights II", [new PhotoInput("p/2.jpg", "https://cdn.test/2.jpg")])));
        Assert.Equal("Aetro Heights II", Assert.Single(await Matches(requirement.Id)).ProjectName);
    }

    // ── 11. At most three videos a listing, atomically ───────────────────────────────────────────────────────

    [Fact]
    public async Task Parallel_video_uploads_never_take_more_than_three_places()
    {
        var flat = await Flat();
        Task<EstateVideoUploadDto> Start() =>
            host.SendAs(CompanyA, new StartEstateVideoUploadCommand(EstateListingKind.Property, flat.Id, "walk.mp4", 10L << 20, "video/mp4", null));
        await Start();
        await Start();

        var tries = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            try
            {
                await Start();
                return true;
            }
            catch (ValidatorException)
            {
                return false;
            }
        }));
        Assert.Equal(1, tries.Count(t => t));
        List<EstateVideoDocument> rows = [];
        await host.WithAsync(CompanyA, async sp => rows = await Collection<EstateVideoDocument>(sp, "re_videos").Find(v => !v.IsDeleted).ToListAsync());
        Assert.Equal([0, 1, 2], rows.Select(r => r.Slot!.Value).Order());
        // Every upload made in media for a loser was aborted.
        Assert.Equal(host.Media.Created.Count - 3, host.Media.Aborted.Count);
    }

    // ── 12. The watch sweep goes on past a failing row ──────────────────────────────────────────────────────

    [Fact]
    public async Task One_failing_watch_does_not_stop_the_sweep()
    {
        var bad = await host.SendAs(CompanyA, new CloseRequirementCommand((await Requirement(lead: "lead-bad")).Id,
            new CloseRequirementInput(RequirementStatus.Closed, null, RequirementCloseReason.NotAvailable)));
        var good = await host.SendAs(CompanyA, new CloseRequirementCommand((await Requirement(lead: "lead-good")).Id,
            new CloseRequirementInput(RequirementStatus.Closed, null, RequirementCloseReason.NotAvailable)));
        await Change<RequirementDocument>("re_requirements", good.Id, r => r.WatchUntil = DateTime.UtcNow.AddDays(-1));
        // A broken row (no company): its update fails.
        await Change<RequirementDocument>("re_requirements", bad.Id, r =>
        {
            r.WatchUntil = DateTime.UtcNow.AddDays(-2);
            r.CompanyId = string.Empty;
        });

        var ended = -1;
        await host.RunAsJobAsync(async sp => ended = await sp.GetRequiredService<WatchedDemand>().EndWatchesAsync(CancellationToken.None));
        Assert.Equal(1, ended);
        Assert.Equal(RequirementStatus.Closed, (await host.SendAs(CompanyA, new GetRequirementQuery(good.Id))).Status);
        await host.RunAsJobAsync(async sp => ended = await sp.GetRequiredService<WatchedDemand>().EndWatchesAsync(CancellationToken.None));
        Assert.Equal(0, ended);   // only the broken one is left: the job's loop stops
    }

    // ── 13. CP terms for PBRM ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_pbrm_member_reads_the_cp_terms_and_a_save_keeps_them()
    {
        static ProjectInput Input(CpTermsInput? cp, string? overview = null) => new()
        {
            Name = "Green Valley", Kind = ProjectKind.Partner, Locality = "Sector 115", BuilderName = "GV Builders", Stage = ProjectStage.NewLaunch,
            Categories = [ProjectCategory.Residential], CpTerms = cp, Overview = overview,
        };
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, Input(new CpTermsInput(2m, null, "On registry", 90, null, null))));
        var read = await host.SendAsMember(CompanyA, "agent-b", [P.ProjectManage, P.BrokerageManage], new GetProjectQuery(project.Id));
        Assert.Equal(2m, read.CpTerms!.CommissionPercent);
        Assert.Null((await host.SendAsMember(CompanyA, "agent-p", [P.ProjectManage], new GetProjectQuery(project.Id))).CpTerms);

        var cp = read.CpTerms;
        await host.SendAsMember(CompanyA, "agent-b", [P.ProjectManage, P.BrokerageManage], new SaveProjectCommand(project.Id,
            Input(new CpTermsInput(cp.CommissionPercent, null, cp.PaidWhen, cp.LeadValidityDays, cp.ValidUntil, cp.Notes), "Edited")));
        Assert.Equal(2m, (await host.SendAs(CompanyA, new GetProjectQuery(project.Id))).CpTerms!.CommissionPercent);
    }

    // ── 15. A typed TDS % is told from the rule's ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_typed_tds_percent_is_marked_manual_and_a_save_without_one_applies_the_rule_again()
    {
        var owner = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "Harpreet Singh", Phone = "9814000001" }));
        var buyer = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "Anjali Sharma", Phone = "9814000002" }));
        var flat = await Flat();
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(flat.Id, [new PropertyOwnerInput(owner.Id, null, HoldingKind.RegisteredSaleDeed)]));
        var deal = await host.SendAs(CompanyA, new SaveDealCommand(null, new DealInput { PropertyId = flat.Id, BuyerIds = [buyer.Id], Price = 5000000m }));

        var typed = await host.SendAs(CompanyA, new SaveBrokerageCommand(null,
            new BrokerageInput { DealId = deal.Id, Side = BrokerageSide.Seller, Basis = BrokerageBasis.Fixed, Rate = 40000m, TdsPercent = 2m }));
        Assert.Equal((2m, true), (typed.TdsPercent, typed.TdsManual));
        var rule = await host.SendAs(CompanyA, new SaveBrokerageCommand(typed.Id,
            new BrokerageInput { DealId = deal.Id, Side = BrokerageSide.Seller, Basis = BrokerageBasis.Fixed, Rate = 40000m }));
        Assert.Equal((0m, false), (rule.TdsPercent, rule.TdsManual));   // an individual payer: none by the rule
    }

    // ── 16. Clearing a party's residency or gender ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_partys_residency_and_gender_can_be_cleared_and_null_keeps_them()
    {
        var party = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput
        {
            Name = "Anjali Sharma", Phone = "9814000002", Residency = PartyResidency.Nri, Gender = PartyGender.Female,
        }));
        var kept = await host.SendAs(CompanyA, new SavePartyCommand(party.Id, new PartyInput { Name = "Anjali Sharma", Phone = "9814000002" }));
        Assert.Equal((PartyResidency.Nri, PartyGender.Female), (kept.Residency!.Value, kept.Gender!.Value));
        var noResidency = await host.SendAs(CompanyA, new SavePartyCommand(party.Id, new PartyInput { Name = "Anjali Sharma", Phone = "9814000002", ClearResidency = true }));
        Assert.Equal(((PartyResidency?)null, PartyGender.Female), (noResidency.Residency, noResidency.Gender!.Value));
        var none = await host.SendAs(CompanyA, new SavePartyCommand(party.Id, new PartyInput { Name = "Anjali Sharma", Phone = "9814000002", ClearGender = true }));
        Assert.Null(none.Gender);
    }
}
