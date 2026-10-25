using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Requirements;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// M1 matching (D9, D10): the customer who came before the property is remembered and told when it arrives; both ways;
/// hard filters and the score; budget tolerance; shown or rejected never again; agents see only their own.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class MatchingTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private static RequirementInput WantsPlot(string lead = "lead-1", string name = "Rahul") => new()
    {
        LeadId = lead, LeadName = name, LeadPhone = "9811122233", Purpose = PropertyPurpose.Sale, Types = [PropertyType.Plot],
        Localities = ["Sector 70", "Sector 71"], BudgetMax = 12500000m, SizeMin = new AreaInput(180, AreaUnit.SqYd), Facings = [Facing.East, Facing.North],
    };

    private static PropertyInput Plot(string locality = "Sector 70", decimal price = 12000000m, Facing facing = Facing.East, decimal gaj = 200) => new()
    {
        Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, Locality = locality, Price = price, Facing = facing, PlotArea = new AreaInput(gaj, AreaUnit.SqYd),
    };

    [Fact]
    public async Task A_customer_who_came_before_the_property_is_told_when_it_arrives()
    {
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, WantsPlot()));
        Assert.Equal(0, requirement.NewMatches);   // nothing in stock yet — still saved and active
        Assert.Empty(host.Bells.Sent);

        var plot = await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot()));
        var match = Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));
        Assert.Equal((plot.Id, MatchStatus.New), (match.PropertyId, match.Status));
        Assert.Contains("Sector 70 (first choice)", match.Reasons);
        Assert.Contains("East facing", match.Reasons);
        Assert.Equal(1, (await host.SendAs(CompanyA, new GetPropertyQuery(plot.Id))).OpenMatches);

        var bell = Assert.Single(host.Bells.Sent);
        Assert.Equal(["user-test"], bell.Notification.Recipients);
        Assert.Equal("P-0001 fits Rahul", bell.Notification.Title);
    }

    [Fact]
    public async Task A_new_requirement_finds_the_stock_that_fits_and_skips_what_does_not()
    {
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot()));                                // fits
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot("Sector 71", 13500000m)));          // 8% over: within the 10% tolerance
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot("Sector 71", 14000000m)));          // 12% over: out
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot(facing: Facing.South)));           // wrong facing
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot(gaj: 150)));                        // too small
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot("Aerocity")));                      // another locality
        await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput                        // for rent
        {
            Purpose = PropertyPurpose.Rent, Type = PropertyType.Plot, Locality = "Sector 70", Rent = 50000, Facing = Facing.East,
        }));

        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, WantsPlot()));
        var matches = await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null));

        Assert.Equal(2, matches.Count);
        Assert.True(matches[0].Score > matches[1].Score);
        Assert.Contains("8% over budget", matches[1].Reasons);
        Assert.Equal(2, requirement.NewMatches);
        Assert.Equal("2 properties fit Rahul", Assert.Single(host.Bells.Sent).Notification.Title);
    }

    [Fact]
    public async Task A_missing_fact_costs_points_instead_of_hiding_the_property()
    {
        var unknown = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, Locality = "Sector 70", Price = 12000000m,
        }));
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, WantsPlot()));
        var match = Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));
        Assert.Equal(unknown.Id, match.PropertyId);
        Assert.Equal(80, match.Score);
        Assert.Contains("Facing not filled", match.Reasons);
        Assert.Contains("Size not filled", match.Reasons);
    }

    [Fact]
    public async Task Shown_or_rejected_matches_are_never_offered_again_and_new_ones_follow_the_property()
    {
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, WantsPlot()));
        var plot = await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot()));
        var match = Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));

        await host.SendAs(CompanyA, new UpdateMatchCommand(match.Id, new MatchUpdateInput(MatchStatus.Rejected, "Too far from school")));
        await host.SendAs(CompanyA, new SavePropertyCommand(plot.Id, Plot(price: 11000000m)));   // price down: matched again, but this pair is history
        var after = Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));
        Assert.Equal((MatchStatus.Rejected, "Too far from school"), (after.Status, after.RejectReason));
        Assert.Single(host.Bells.Sent);

        // A second plot is matched; when it is sold its new match is dropped.
        var second = await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot("Sector 71")));
        Assert.Equal(2, (await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null))).Count);
        await host.SendAs(CompanyA, new ConfirmPropertyCommand(second.Id, new ConfirmPropertyInput(PropertyStatus.Sold, null, ConfirmedVia.Call, null)));
        Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));
    }

    [Fact]
    public async Task Closing_a_requirement_drops_its_new_matches_and_reopening_finds_them_again()
    {
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot()));
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, WantsPlot()));

        var closed = await host.SendAs(CompanyA, new CloseRequirementCommand(requirement.Id, new CloseRequirementInput(RequirementStatus.Closed, "Bought elsewhere")));
        Assert.Equal((RequirementStatus.Closed, "Bought elsewhere", 0), (closed.Status, closed.CloseReason, closed.NewMatches));

        var reopened = await host.SendAs(CompanyA, new CloseRequirementCommand(requirement.Id, new CloseRequirementInput(RequirementStatus.Active, null)));
        Assert.Equal(1, reopened.NewMatches);
    }

    [Fact]
    public async Task A_stale_property_is_not_matched_until_it_is_confirmed()
    {
        var plot = await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot()));
        await host.WithAsync(CompanyA, async sp =>
        {
            var repo = sp.GetRequiredService<IPropertyRepository>();
            var doc = await repo.GetDocument(plot.Id).FirstAsync();
            doc.LastConfirmedAt = DateTime.UtcNow.AddDays(-40);
            await repo.UpdateAsync(doc);
        });
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, WantsPlot()));
        Assert.Equal(0, requirement.NewMatches);

        await host.SendAs(CompanyA, new ConfirmPropertyCommand(plot.Id, new ConfirmPropertyInput(PropertyStatus.Available, null, ConfirmedVia.Call, null)));
        Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));
    }

    [Fact]
    public async Task An_agent_works_only_their_own_customers_and_a_manager_sees_all()
    {
        var agentCodes = new[] { EstatePermissions.RequirementManage, EstatePermissions.PropertyView };
        var mine = await host.SendAsMember(CompanyA, "agent-1", agentCodes, new SaveRequirementCommand(null, WantsPlot("lead-1", "Rahul")));
        var theirs = await host.SendAsMember(CompanyA, "agent-2", agentCodes, new SaveRequirementCommand(null, WantsPlot("lead-2", "Simran")));
        Assert.Equal("agent-1", mine.AgentId);

        Assert.Equal(["Rahul"], (await host.SendAsMember(CompanyA, "agent-1", agentCodes, new GetRequirementsQuery(null, null, null, null))).Select(r => r.LeadName));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-1", agentCodes, new GetRequirementQuery(theirs.Id)));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-1", agentCodes,
            new SaveRequirementCommand(null, new RequirementInput { LeadId = "lead-3", AgentId = "agent-2", Purpose = PropertyPurpose.Rent })));

        await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot()));
        Assert.Single(await host.SendAsMember(CompanyA, "agent-1", agentCodes, new GetMatchesQuery(null, null, null)));
        Assert.Equal(2, (await host.SendAs(CompanyA, new GetMatchesQuery(null, null, null))).Count);   // the manager
        Assert.Equal(["agent-1", "agent-2"], host.Bells.Sent.SelectMany(b => b.Notification.Recipients).Order());
    }

    [Fact]
    public async Task Rent_matches_tenant_type_floor_and_furnishing()
    {
        var family = new RequirementInput
        {
            LeadId = "lead-9", LeadName = "Neha", Purpose = PropertyPurpose.Rent, Types = [PropertyType.Flat, PropertyType.BuilderFloor], BudgetMax = 40000,
            Bhk = [3], Floor = FloorPreference.NotTop, TenantType = TenantPreference.Family, Furnishings = [Furnishing.SemiFurnished, Furnishing.FullyFurnished],
        };
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Flat(floor: 2, total: 4, tenants: [TenantPreference.Family])));      // fits
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Flat(floor: 4, total: 4, tenants: [TenantPreference.Family])));      // top floor
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Flat(floor: 1, total: 4, tenants: [TenantPreference.Bachelors])));   // bachelors only
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Flat(floor: 1, total: 4, tenants: [], bhk: 2)));                     // 2 BHK

        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, family));
        Assert.Equal(1, requirement.NewMatches);
    }

    private static PropertyInput Flat(int floor, int total, IReadOnlyList<TenantPreference> tenants, int bhk = 3) => new()
    {
        Purpose = PropertyPurpose.Rent, Type = PropertyType.BuilderFloor, Locality = "Sector 70", Rent = 35000, Bhk = bhk, Floor = floor,
        TotalFloors = total, TenantPreferences = tenants, Furnishing = Furnishing.SemiFurnished,
    };

    [Fact]
    public async Task The_dashboard_counts_stock_customers_and_matches()
    {
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Plot()));
        await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput { Purpose = PropertyPurpose.Rent, Type = PropertyType.Flat, Locality = "Sector 70", Rent = 20000 }));
        await host.SendAs(CompanyA, new SaveRequirementCommand(null, WantsPlot()));

        var dashboard = await host.SendAs(CompanyA, new GetEstateDashboardQuery());
        Assert.Equal((1, 1, 0, 1, 1, 2), (dashboard.AvailableForSale, dashboard.AvailableForRent, dashboard.Stale, dashboard.ActiveRequirements,
            dashboard.NewMatches, dashboard.AddedThisWeek));
        Assert.Equal("Rahul", Assert.Single(dashboard.LatestMatches).LeadName);
    }

    [Fact]
    public void The_score_rules_out_on_hard_filters()
    {
        var s = new EstateSettingsDocument { Id = string.Empty };
        var p = new PropertyDocument
        {
            Id = "p", Code = "P-1", Locality = "Sector 70", LocalityKey = "sector 70", Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot,
            Status = PropertyStatus.Available, LastConfirmedAt = DateTime.UtcNow, PriceValue = 1_00_00_000m, Corner = false,
        };
        var r = new RequirementDocument { Id = "r", LeadId = "l", AgentId = "a", Purpose = PropertyPurpose.Sale, CornerOnly = true };
        Assert.False(MatchEngine.Score(p, r, s, DateTime.UtcNow).Fits);
        r.CornerOnly = false;
        Assert.Equal(100, MatchEngine.Score(p, r, s, DateTime.UtcNow).Score);
    }
}
