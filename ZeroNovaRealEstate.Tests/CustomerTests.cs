using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Customers;
using ZeroNovaRealEstate.Application.Features.Deals;
using ZeroNovaRealEstate.Application.Features.Enquiries;
using ZeroNovaRealEstate.Application.Features.Parties;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Rentals;
using ZeroNovaRealEstate.Application.Features.Requirements;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Features.Visits;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Customers;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Money;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Settings;
using ZeroNovaRealEstate.Models.Visits;
using static ZeroNovaRealEstate.Tests.TestHost;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// Customers (C2, D32, D39): rows found by contact and by the contact's leads, the contact backfilled when an enquiry names
/// it, the Customers list (counts, filters, paging, an agent's own share, highlighted), the dashboard's permission gating
/// (deals, tenancies, owned properties), and the unmet demand.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class CustomerTests(MongoFixture mongo) : IAsyncLifetime
{
    private static readonly string[] Agent = [P.RequirementManage];
    private static readonly string[] Manager = [P.RequirementManage, P.RequirementView];

    private TestHost host = null!;
    private PropertyDto flat = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali",
            null, "Sharma Properties", "9876012345")));
        flat = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 70", Bhk = 3, SuperArea = new AreaInput(1650, AreaUnit.SqFt),
            Price = 9500000m,
        }));
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    /// <summary>zeronova-lead's lead-enquiry-received for a Real Estate enquiry, as the consumer reads it.</summary>
    private static LeadEnquiryReceivedNotification Enquiry(string externalRef, string leadId, string contactId, string? agent = "agent-b", string name = "Rahul Verma",
        string phone = "+919811122233") => new()
    {
        CompanyId = CompanyA, LeadId = leadId, ContactId = contactId, Name = name, Phone = phone, OccurredAt = DateTimeOffset.UtcNow, Channel = "website",
        ActivityChannel = "Website", ExternalRef = externalRef, ActivityId = "act-" + externalRef, AssignedTo = agent, AssignedToName = agent is null ? null : "Asha",
        Metadata = new() { ["product"] = "REAL_ESTATE", ["form"] = "contact" }, IsNewLead = true,
        MessageId = Guid.NewGuid().ToString("N"), EventType = "lead-enquiry-received", Source = "zeronova-lead", ChangedDate = DateTimeOffset.UtcNow,
    };

    private Task<RequirementDto> Requirement(string leadId, string? contactId = null, string? agent = null, PropertyPurpose purpose = PropertyPurpose.Sale,
        string name = "Rahul Verma", string phone = "9811122233") =>
        host.SendAs(CompanyA, new SaveRequirementCommand(null, new RequirementInput
        {
            LeadId = leadId, ContactId = contactId, LeadName = name, LeadPhone = phone, AgentId = agent, Purpose = purpose, Types = [PropertyType.Flat],
            Localities = ["Sector 70"], Bhk = [3], BudgetMin = 8000000m, BudgetMax = 10000000m,
        }));

    private Task<EstateCustomerPageDto> List(EstateCustomerFilterInput? filter = null, int skip = 0, int take = 50) =>
        host.SendAs(CompanyA, new GetCustomersQuery(filter ?? new EstateCustomerFilterInput(), skip, take));

    private Task<EstateCustomerPageDto> ListAs(string userId, string[] permissions, EstateCustomerFilterInput? filter = null) =>
        host.SendAsMember(CompanyA, userId, permissions, new GetCustomersQuery(filter ?? new EstateCustomerFilterInput()));

    private Task<EstateCustomerDashboardDto> Dashboard(string? contactId, params string[] leadIds) =>
        host.SendAs(CompanyA, new GetCustomerQuery(contactId, leadIds));

    private Task<EstateCustomerDashboardDto> DashboardAs(string userId, string[] permissions, string? contactId, params string[] leadIds) =>
        host.SendAsMember(CompanyA, userId, permissions, new GetCustomerQuery(contactId, leadIds));

    private Task<PartyDto> Party(string name, string phone, string? leadId = null, string? contactId = null) =>
        host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = name, Phone = phone, LeadId = leadId, ContactId = contactId, Address = "Mohali" }));

    // ── The contact link ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rows_are_found_by_leads_until_an_enquiry_names_the_contact_which_is_backfilled_and_merges_the_customer()
    {
        var requirement = await Requirement("lead-1");
        var visit = await host.SendAs(CompanyA, new ScheduleVisitCommand(new VisitInput
        {
            PropertyId = flat.Id, LeadId = "lead-1", LeadName = "Rahul Verma", RequirementId = requirement.Id, At = DateTimeOffset.UtcNow.AddDays(1),
        }));
        Assert.Null(requirement.ContactId);

        // Only the lead is known: the customer is the lead.
        var byLead = await Dashboard(null, "lead-1");
        Assert.Equal(((string?)null, 1, 1), (byLead.ContactId, byLead.Requirements.Count, byLead.Visits.Count));
        var row = Assert.Single((await List()).Items);
        Assert.Equal(((string?)null, "lead-1", "Rahul Verma", "919811122233"), (row.ContactId, Assert.Single(row.LeadIds), row.Name, row.Phone));

        // zeronova-lead announces an enquiry of the lead with its contact: the lead's rows take it, once.
        await host.PublishAsync(Enquiry("cms-1", "lead-1", "contact-1"));
        await host.PublishAsync(Enquiry("cms-1", "lead-1", "contact-1"));   // redelivered
        Assert.Equal("contact-1", (await host.SendAs(CompanyA, new GetRequirementQuery(requirement.Id))).ContactId);
        await host.WithAsync(CompanyA, async sp =>
            Assert.Equal("contact-1", await sp.GetRequiredService<IVisitRepository>().GetDocument(visit.Id).Select(v => v.ContactId).FirstAsync()));

        // One customer now, by the contact (the lead's row merged into it).
        var merged = Assert.Single((await List()).Items);
        Assert.Equal(("contact-1", 1, 1, 1), (merged.ContactId, merged.Counts.Enquiries, merged.Counts.OpenRequirements, merged.Counts.Visits));
        var byContact = await Dashboard("contact-1");
        Assert.Equal(["lead-1"], byContact.LeadIds);
        Assert.Equal((1, 1, 1), (byContact.Enquiries.Count, byContact.Requirements.Count, byContact.Visits.Count));

        // A later requirement for the lead learns the contact from the enquiry; one given by the lead picker is kept.
        Assert.Equal("contact-1", (await Requirement("lead-1")).ContactId);
        var other = await Requirement("lead-9", "contact-9", name: "Neha Kapoor", phone: "9822233344");
        Assert.Equal("contact-9", other.ContactId);
        Assert.Equal(other.Id, Assert.Single((await Dashboard("contact-9")).Requirements).Requirement.Id);
        Assert.Equal(other.Id, Assert.Single((await Dashboard(null, "lead-9")).Requirements).Requirement.Id);

        // The app's leads of the contact (ZeroNova_contactLeads) reach a lead our rows never tied to it.
        await Requirement("lead-old", name: "Rahul Verma");
        Assert.Equal(3, (await Dashboard("contact-1", "lead-old")).Requirements.Count);
        Assert.Equal(3, (await List()).Total);   // contact-1, contact-9, lead-old

        await Assert.ThrowsAsync<ValidatorException>(() => Dashboard(null));
    }

    [Fact]
    public async Task A_party_linked_to_a_lead_brings_its_deals_and_tenancies_to_the_customer()
    {
        await host.PublishAsync(Enquiry("cms-1", "lead-2", "contact-2"));
        var buyer = await Party("Sunita Rani", "9833344455", leadId: "lead-2");
        var seller = await Party("Harpreet Singh", "9844455566");
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(flat.Id, [new PropertyOwnerInput(seller.Id, null, HoldingKind.RegisteredSaleDeed)]));
        await host.SendAs(CompanyA, new SaveDealCommand(null, new DealInput { PropertyId = flat.Id, BuyerIds = [buyer.Id], Price = 9500000m }));

        Assert.Equal("contact-2", buyer.ContactId);   // learned from the enquiry of its lead
        var customer = await Dashboard("contact-2");
        Assert.Equal((1, 1), (customer.Enquiries.Count, customer.Deals!.Count));
        Assert.Equal(1, Assert.Single((await List(new EstateCustomerFilterInput { Text = "sunita" })).Items).Counts.Deals);
    }

    // ── The list ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_counts_filters_pages_and_shows_an_agent_their_own_share()
    {
        // C1: agent-a's, matching the flat (a new match → highlighted). C2: agent-b's, lost. C3: both agents', and agent-b's enquiry.
        var c1 = await Requirement("lead-a1", "contact-a1", "agent-a", name: "Aman Arora", phone: "9800000001");
        Assert.True(c1.NewMatches > 0);
        var c2 = await Requirement("lead-b1", "contact-b1", "agent-b", PropertyPurpose.Rent, "Bhavna Bedi", "9800000002");
        await host.SendAs(CompanyA, new CloseRequirementCommand(c2.Id, new CloseRequirementInput(RequirementStatus.Lost, "Went with another dealer")));
        await Requirement("lead-ab", "contact-ab", "agent-a", PropertyPurpose.Rent, "Chetan Chopra", "9800000003");
        await Requirement("lead-ab", "contact-ab", "agent-b", PropertyPurpose.Rent, "Chetan Chopra", "9800000003");
        await host.PublishAsync(Enquiry("cms-ab", "lead-ab", "contact-ab", "agent-b", "Chetan Chopra", "+919800000003"));

        var everyone = await List();
        Assert.Equal(3, everyone.Total);
        Assert.Equal("contact-ab", everyone.Items[0].ContactId);   // the latest activity first
        var c3 = everyone.Items.Single(c => c.ContactId == "contact-ab");
        Assert.Equal(new EstateCustomerCountsDto(1, 2, 0, 0), c3.Counts);
        Assert.Equal((true, 1), (everyone.Items.Single(c => c.ContactId == "contact-a1").Highlighted, everyone.Items.Single(c => c.ContactId == "contact-b1").Unmet));

        // An agent: the customers they are agent of, with only their own share; no deals without PDLV / PDLM.
        var mine = await ListAs("agent-a", Agent);
        Assert.Equal(["contact-a1", "contact-ab"], mine.Items.Select(c => c.ContactId!).Order());
        Assert.Equal(new EstateCustomerCountsDto(0, 1, 0, null), mine.Items.Single(c => c.ContactId == "contact-ab").Counts);
        Assert.Equal(["contact-ab", "contact-b1"], (await ListAs("agent-b", Agent)).Items.Select(c => c.ContactId!).Order());
        Assert.Empty((await ListAs("agent-b", Agent, new EstateCustomerFilterInput { Highlighted = true })).Items);   // agent-a's match
        Assert.All((await ListAs("agent-b", [P.RequirementManage, P.DealManage])).Items, c => Assert.Equal(0, c.Counts.Deals));
        // An agent filter is a manager's; an agent's own list ignores it.
        Assert.Equal(2, (await ListAs("agent-a", Agent, new EstateCustomerFilterInput { AgentId = "agent-b" })).Total);
        Assert.Equal(2, (await ListAs("manager", Manager, new EstateCustomerFilterInput { AgentId = "agent-b" })).Total);

        // Filters.
        Assert.Equal(["contact-b1"], (await List(new EstateCustomerFilterInput { HasOpenRequirement = false })).Items.Select(c => c.ContactId!));
        Assert.Equal(2, (await List(new EstateCustomerFilterInput { HasOpenRequirement = true })).Total);
        Assert.Equal(["contact-a1"], (await List(new EstateCustomerFilterInput { Highlighted = true })).Items.Select(c => c.ContactId!));
        Assert.Equal(2, (await List(new EstateCustomerFilterInput { Highlighted = false })).Total);
        Assert.Equal(["contact-ab"], (await List(new EstateCustomerFilterInput { Text = "chetan" })).Items.Select(c => c.ContactId!));
        Assert.Equal(["contact-b1"], (await List(new EstateCustomerFilterInput { Text = "9800000002" })).Items.Select(c => c.ContactId!));
        Assert.Equal(0, (await List(new EstateCustomerFilterInput { LastActivityFrom = DateTimeOffset.UtcNow.AddHours(1) })).Total);
        Assert.Equal(3, (await List(new EstateCustomerFilterInput { LastActivityFrom = DateTimeOffset.UtcNow.AddHours(-1), LastActivityTo = DateTimeOffset.UtcNow.AddHours(1) })).Total);
        Assert.Equal(0, (await ListAs("agent-a", Agent, new EstateCustomerFilterInput { LastActivityTo = DateTimeOffset.UtcNow.AddHours(-1) })).Total);

        // Paging keeps the total.
        var page = await List(skip: 1, take: 1);
        Assert.Equal((3, everyone.Items[1].ContactId), (page.Total, Assert.Single(page.Items).ContactId));
    }

    [Fact]
    public async Task A_company_first_list_builds_the_summaries_from_its_rows()
    {
        await Requirement("lead-1", "contact-1");
        await Requirement("lead-2");
        await host.PublishAsync(Enquiry("cms-3", "lead-3", "contact-3"));
        // As if the rows came before C2: no summary yet.
        await host.WithAsync(CompanyA, async sp =>
        {
            var customers = sp.GetRequiredService<ICustomerRepository>();
            await customers.RemoveKeysAsync(await customers.GetDocuments().Select(c => c.Key).ToListAsync());
        });

        var list = await List();
        Assert.Equal(["contact-1", "contact-3", "lead:lead-2"], list.Items.Select(c => c.ContactId ?? "lead:" + c.LeadIds[0]).Order());
        Assert.Empty((await host.SendAs(CompanyB, new GetCustomersQuery(new EstateCustomerFilterInput()))).Items);   // another company's own
    }

    [Fact]
    public async Task A_customer_known_only_as_a_party_shows_the_phone_only_with_POWV()
    {
        await Party("Kiran Bala", "9855566677", contactId: "contact-k");
        var row = Assert.Single((await ListAs("manager", Manager)).Items);
        Assert.Equal(("contact-k", "Kiran Bala", (string?)null), (row.ContactId, row.Name, row.Phone));
        Assert.Equal("919855566677", Assert.Single((await ListAs("manager", [.. Manager, P.OwnerView])).Items).Phone);
        Assert.Null((await DashboardAs("manager", Manager, "contact-k")).Phone);
    }

    // ── The dashboard ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_dashboard_shows_deals_tenancies_and_owned_properties_only_with_their_permissions()
    {
        var plot = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, City = "Mohali", Locality = "Sector 82", PlotArea = new AreaInput(200, AreaUnit.SqYd), Price = 12000000m,
        }));
        var shop = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Rent, Type = PropertyType.Shop, City = "Mohali", Locality = "Phase 7", Rent = 40000m,
        }));
        var customer = await Party("Sunita Rani", "9833344455", "lead-d", "contact-d");
        var owner = await Party("Harpreet Singh", "9844455566");
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(flat.Id, [new PropertyOwnerInput(owner.Id, null, HoldingKind.RegisteredSaleDeed)]));
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(shop.Id, [new PropertyOwnerInput(owner.Id, null, HoldingKind.RegisteredSaleDeed)]));
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(plot.Id, [new PropertyOwnerInput(customer.Id, null, HoldingKind.RegisteredSaleDeed)]));
        var requirement = await Requirement("lead-d", "contact-d", "agent-x", name: "Sunita Rani", phone: "9833344455");
        await host.SendAs(CompanyA, new SaveDealCommand(null, new DealInput
        {
            PropertyId = flat.Id, BuyerIds = [customer.Id], RequirementId = requirement.Id, LeadId = "lead-d", Price = 9500000m,
            Payments = [new DealPaymentInput(PaymentMode.Rtgs, 500000m, "UTR1", null, "2026-11-01", customer.Id)],
        }));
        await host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
        {
            PropertyId = shop.Id, TenantIds = [customer.Id], Rent = 40000m, StartDate = "2026-11-01", Months = 11, RentDueDay = 5,
        }));

        // The dealer (every permission): all of it, with the deal's money.
        var full = await Dashboard("contact-d");
        var deal = Assert.Single(full.Deals!);
        Assert.Equal((9500000m, 500000m, 9000000m), (deal.Price, deal.Paid, deal.Balance));
        Assert.Equal((1, 1, 1), (full.Tenancies!.Count, full.OwnedProperties!.Count, full.Numbers.Deals));
        Assert.Equal((plot.Id, 100m), (full.OwnedProperties[0].PropertyId, full.OwnedProperties[0].SharePercent));
        Assert.Equal([new CustomerStageCountDto(DealStage.Offer, 1)], full.Numbers.DealsByStage);
        Assert.Equal((1, 1, 1), (full.Numbers.Tenancies, full.Numbers.PropertiesOwned, full.Numbers.RequirementsActive));

        // The requirement's agent with PRQM only: their requirement, nothing of deals, tenancies or owners.
        var agent = await DashboardAs("agent-x", Agent, "contact-d");
        Assert.Equal((1, (IReadOnlyList<DealDto>?)null, (IReadOnlyList<TenancyDto>?)null), (agent.Requirements.Count, agent.Deals, agent.Tenancies));
        Assert.Null(agent.OwnedProperties);
        Assert.Equal(((int?)null, (int?)null, (int?)null), (agent.Numbers.Deals, agent.Numbers.Tenancies, agent.Numbers.PropertiesOwned));
        Assert.Null(agent.Numbers.DealsByStage);

        // PDLM alone is one's own deals (the deal is the dealer's); PDLV every one. The same for tenancies; owners with POWV.
        Assert.Empty((await DashboardAs("agent-x", [.. Agent, P.DealManage], "contact-d")).Deals!);
        Assert.Single((await DashboardAs("agent-x", [.. Agent, P.DealView], "contact-d")).Deals!);
        Assert.Empty((await DashboardAs("agent-x", [.. Agent, P.RentalManage], "contact-d")).Tenancies!);
        Assert.Single((await DashboardAs("agent-x", [.. Agent, P.RentalView], "contact-d")).Tenancies!);
        Assert.Single((await DashboardAs("agent-x", [.. Agent, P.OwnerView], "contact-d")).OwnedProperties!);

        // Another agent sees none of agent-x's requirement, matches or shares.
        var stranger = await DashboardAs("agent-y", Agent, "contact-d");
        Assert.Equal((0, 0, 0), (stranger.Requirements.Count, stranger.Matches.Count, stranger.Numbers.RequirementsActive));
    }

    [Fact]
    public async Task The_dashboard_has_matches_shares_visits_and_the_numbers_on_top()
    {
        var requirement = await Requirement("lead-1", "contact-1");
        await host.SendAs(CompanyA, new CreateShareCommand(flat.Id, requirement.Id));
        await host.SendAs(CompanyA, new ScheduleVisitCommand(new VisitInput
        {
            PropertyId = flat.Id, LeadId = "lead-1", RequirementId = requirement.Id, At = DateTimeOffset.UtcNow.AddDays(2),
        }));
        await host.PublishAsync(Enquiry("cms-1", "lead-1", "contact-1", agent: null));

        var d = await Dashboard("contact-1");
        var match = Assert.Single(d.Matches);
        Assert.Equal((flat.Id, MatchStatus.Shown), (match.PropertyId, match.Status));   // the share marked it shown
        Assert.Equal(new CustomerMatchCountsDto(0, 1, 0, 0), d.Numbers.Matches);
        var share = Assert.Single(d.Shares);
        Assert.Equal((flat.Code, requirement.Id, 0), (share.PropertyCode, share.RequirementId, share.Opens));
        Assert.Contains("/p/", share.Url);
        Assert.Equal((1, 0, 1, 0, 1, 1), (d.Numbers.SharesSent, d.Numbers.SharesOpened, d.Numbers.VisitsUpcoming, d.Numbers.VisitsDone, d.Numbers.Enquiries,
            d.Numbers.OpenEnquiries));
        Assert.Equal(1, Assert.Single(d.Requirements).Matches);
        Assert.Contains("3 BHK flat for sale", d.Requirements[0].Summary);
        Assert.False(d.Highlighted);   // the only match was shown

        // An unassigned enquiry is a manager's (D39): the agent of the requirement does not see it.
        Assert.Empty((await DashboardAs("user-test", Agent, "contact-1")).Enquiries);
        Assert.Single((await DashboardAs("user-test", Manager, "contact-1")).Enquiries);
    }

    [Fact]
    public async Task Unmet_demand_lists_requirements_not_available_or_lost_and_enquiries_not_available()
    {
        var notAvailable = await Requirement("lead-1", "contact-1");
        var lost = await Requirement("lead-1", "contact-1", purpose: PropertyPurpose.Rent);
        var bought = await Requirement("lead-1", "contact-1");
        await host.SendAs(CompanyA, new CloseRequirementCommand(notAvailable.Id, new CloseRequirementInput(RequirementStatus.Closed, "Not available in this budget")));
        await host.SendAs(CompanyA, new CloseRequirementCommand(lost.Id, new CloseRequirementInput(RequirementStatus.Lost, "Bought elsewhere")));
        await host.SendAs(CompanyA, new CloseRequirementCommand(bought.Id, new CloseRequirementInput(RequirementStatus.Closed, "Bought P-0009 through us")));
        await host.PublishAsync(Enquiry("cms-1", "lead-1", "contact-1", agent: "user-test"));
        await host.PublishAsync(Enquiry("cms-2", "lead-1", "contact-1", agent: "user-test"));
        var enquiries = (await host.SendAs(CompanyA, new GetEnquiriesQuery(new EnquiryFilterInput { ContactId = "contact-1" }))).Items;
        await host.SendAs(CompanyA, new UpdateEnquiryCommand(enquiries[0].Id, new UpdateEnquiryInput
        {
            Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.NotAvailable, Wanted = "2 BHK in Sector 70 under 60 lakh",
        }));
        await host.SendAs(CompanyA, new UpdateEnquiryCommand(enquiries[1].Id, new UpdateEnquiryInput
        {
            Status = EnquiryStatus.Closed, CloseReason = EnquiryCloseReason.NotInterested,
        }));

        var d = await Dashboard("contact-1");
        Assert.Equal(3, d.Numbers.Unmet);
        Assert.Equal(new[] { enquiries[0].Id, lost.Id, notAvailable.Id }.Order(), d.Unmet.Select(u => u.Id).Order());
        var wanted = d.Unmet.Single(u => u.Kind == CustomerUnmetKind.Enquiry);
        Assert.Equal(("2 BHK in Sector 70 under 60 lakh", enquiries[0].Code, EnquiryCloseReason.NotAvailable), (wanted.What, wanted.Code, wanted.EnquiryCloseReason));
        var asked = d.Unmet.Single(u => u.Id == notAvailable.Id);
        Assert.Equal(("3 BHK flat for sale · Sector 70, Mohali · ₹80,00,000 – ₹1,00,00,000", "Not available in this budget", RequirementStatus.Closed),
            (asked.What, asked.Reason, asked.RequirementStatus));
        Assert.Equal(RequirementStatus.Lost, d.Unmet.Single(u => u.Id == lost.Id).RequirementStatus);

        Assert.Equal(3, Assert.Single((await List()).Items).Unmet);
    }
}
