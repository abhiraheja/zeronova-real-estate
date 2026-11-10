using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Features.Projects;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Requirements;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Features.Visits;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Settings;
using ZeroNovaRealEstate.Models.Visits;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// Projects P1 (design 7, D17–D26): own and partner projects, unit types, generated units kept in step, the unit board
/// and its statuses, price lists with versions and the cost sheet, payment plans, matching by unit type, freshness of a
/// partner project, CP terms behind PBRV, the project share page, and the delete rules.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class ProjectTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali",
            "PBRERA-SAS79-REA0123", "Sharma Properties", "9876012345")));
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private static ProjectInput Own(string name = "Aetro Heights", string locality = "Sector 82") => new()
    {
        Name = name, Kind = ProjectKind.Own, Locality = locality, Categories = [ProjectCategory.Residential, ProjectCategory.GroupHousing],
        Stage = ProjectStage.UnderConstruction, PossessionDate = "2028-12-31", Amenities = ["Club", "Pool"], GstPercent = 5m,
        Towers = [new TowerInput("A", 12), new TowerInput("B", 12)], Reras = [new ReraInput("PBRERA-SAS80-PR0999", "Punjab", "2025-01-10", "2029-12-31", null)],
        Photos = [new PhotoInput("real-estate/p/1.jpg", "https://cdn.test/1.jpg")], Brochure = new MediaFileInput("real-estate/p/b.pdf", "https://cdn.test/b.pdf", "Brochure"),
    };

    private static ProjectInput Partner() => new()
    {
        Name = "Green Valley", Kind = ProjectKind.Partner, Locality = "Sector 115", BuilderName = "GV Builders", BuilderContactName = "Mr Gill",
        BuilderPhone = "9812300000", Stage = ProjectStage.NewLaunch, Categories = [ProjectCategory.Residential],
        CpTerms = new CpTermsInput(2m, null, "On registry", 90, null, "Paid within 30 days of registry"),
    };

    private static UnitTypeInput ThreeBhk(int bhk = 3) => new()
    {
        Name = "3 BHK Type A", Type = PropertyType.Flat, Bhk = bhk, Bathrooms = 3, Balconies = 2,
        SuperArea = new AreaInput(1250, AreaUnit.SqFt), CarpetArea = new AreaInput(900, AreaUnit.SqFt),
    };

    private async Task<(ProjectDto Project, UnitTypeDto Type)> ProjectWithType(ProjectInput? input = null)
    {
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, input ?? Own()));
        var type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, ThreeBhk()));
        return (project, type);
    }

    private Task<IReadOnlyList<UnitDto>> Generate(string projectId, string typeId, int from, int to, int perFloor = 2, string tower = "A") =>
        host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput
        {
            ProjectId = projectId, UnitTypeId = typeId, Tower = tower, FloorFrom = from, FloorTo = to, UnitsPerFloor = perFloor, Pattern = "{tower}-{floor}{nn}",
        }));

    private static RequirementInput Wants3Bhk(string lead = "lead-1", string name = "Rahul", bool cornerOnly = false) => new()
    {
        LeadId = lead, LeadName = name, Purpose = PropertyPurpose.Sale, Types = [PropertyType.Flat], Localities = ["Sector 82"], Bhk = [3],
        BudgetMax = 8000000m, CornerOnly = cornerOnly,
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
    public async Task Own_and_partner_projects_keep_their_facts_and_cp_terms_stay_behind_brokerage()
    {
        var own = await host.SendAs(CompanyA, new SaveProjectCommand(null, Own()));
        Assert.Equal(("PJ-0001", ProjectKind.Own, "Mohali", false), (own.Code, own.Kind, own.City, own.Stale));
        Assert.Null(own.BuilderName);
        Assert.Equal("Brochure", own.Brochure!.Name);
        Assert.Equal(["A", "B"], own.Towers.Select(t => t.Name));

        var partner = await host.SendAs(CompanyA, new SaveProjectCommand(null, Partner()));
        Assert.Equal(("PJ-0002", "GV Builders", "919812300000"), (partner.Code, partner.BuilderName, partner.BuilderPhone));
        Assert.Equal(2m, partner.CpTerms!.CommissionPercent);

        // Without PBRV the terms are not shown; without PBRM a save leaves them as they were.
        var agent = new[] { EstatePermissions.PropertyView, EstatePermissions.ProjectManage };
        Assert.Null((await host.SendAsMember(CompanyA, "agent-1", agent, new GetProjectQuery(partner.Id))).CpTerms);
        var changed = new ProjectInput
        {
            Name = "Green Valley", Kind = ProjectKind.Partner, Locality = "Sector 115", BuilderName = "GV Builders", CpTerms = new CpTermsInput(5m, null, null, null, null, null),
        };
        await host.SendAsMember(CompanyA, "agent-1", agent, new SaveProjectCommand(partner.Id, changed));
        Assert.Equal(2m, (await host.SendAs(CompanyA, new GetProjectQuery(partner.Id))).CpTerms!.CommissionPercent);

        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = "No builder", Kind = ProjectKind.Partner, Locality = "Sector 1",
        })));
        var list = await host.SendAs(CompanyA, new GetProjectsQuery(new ProjectSearchInput { Text = "green" }));
        Assert.Equal("Green Valley", Assert.Single(list).Name);
    }

    [Fact]
    public async Task Units_are_generated_by_pattern_never_twice_and_follow_their_type_and_project()
    {
        var (project, type) = await ProjectWithType();
        var units = await Generate(project.Id, type.Id, 0, 2);
        Assert.Equal(["A-G01", "A-G02", "A-101", "A-102", "A-201", "A-202"], units.Select(u => u.UnitNumber));
        Assert.All(units, u => Assert.Equal(UnitStatus.Available, u.Status));

        // Again on the same floors: refused, and nothing made.
        var again = await Assert.ThrowsAsync<ValidatorException>(() => Generate(project.Id, type.Id, 2, 3));
        Assert.Contains("A-201", again.Message);
        Assert.Equal(6, (await host.SendAs(CompanyA, new GetProjectUnitsQuery(project.Id, null, null, null))).Count);
        await Assert.ThrowsAsync<ValidatorException>(() => Generate(project.Id, type.Id, 3, 3, tower: "C"));   // no such tower

        // A unit is a property: it takes its type's and its project's fields.
        var unit = await host.SendAs(CompanyA, new GetPropertyQuery(units[2].Id));
        Assert.Equal((PropertyType.Flat, 3, 1250m, "Sector 82", "Aetro Heights", "3 BHK Type A in Aetro Heights", ConstructionStatus.UnderConstruction, 12),
            (unit.Type, unit.Bhk, unit.SizeSqFt, unit.Locality, unit.Society, unit.Title, unit.Construction, unit.TotalFloors));
        Assert.Equal(("A-101", project.Id, type.Id, "A", 1, false), (unit.UnitNumber, unit.ProjectId, unit.UnitTypeId, unit.Tower, unit.Floor, unit.Stale));

        // The type and the project change: every unit follows.
        await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, type.Id, ThreeBhk(bhk: 4)));
        var moved = Own(locality: "Sector 83");
        await host.SendAs(CompanyA, new SaveProjectCommand(project.Id, moved));
        unit = await host.SendAs(CompanyA, new GetPropertyQuery(units[2].Id));
        Assert.Equal((4, "Sector 83"), (unit.Bhk, unit.Locality));

        // One unit added by hand, its own fields kept; its number may not repeat.
        var corner = await host.SendAs(CompanyA, new SaveUnitCommand(null, new UnitInput
        {
            ProjectId = project.Id, UnitTypeId = type.Id, UnitNumber = "B-1201", Tower = "B", Floor = 12, Corner = true, Facing = Facing.East,
        }));
        Assert.True(corner.Corner);
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveUnitCommand(null, new UnitInput
        {
            ProjectId = project.Id, UnitTypeId = type.Id, UnitNumber = "b-1201", Tower = "B", Floor = 12,
        })));
        // A unit is changed from its project, not as a property.
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SavePropertyCommand(corner.Id, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, Locality = "Sector 83",
        })));

        var plots = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput
        {
            Name = "250 sq yd plot", Type = PropertyType.Plot, PlotArea = new AreaInput(250, AreaUnit.SqYd),
        }));
        var numbered = await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput
        {
            ProjectId = project.Id, UnitTypeId = plots.Id, Count = 3, Pattern = "Plot {n}", StartNumber = 10,
        }));
        Assert.Equal(["Plot 10", "Plot 11", "Plot 12"], numbered.Select(u => u.UnitNumber));
    }

    [Fact]
    public async Task A_blocked_unit_is_held_for_its_lead_and_the_hold_sweep_releases_it()
    {
        var (project, type) = await ProjectWithType();
        var units = await Generate(project.Id, type.Id, 1, 1);
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SetUnitStatusCommand(units[0].Id,
            new UnitStatusInput(UnitStatus.Blocked, "2020-01-01", null, null))));   // for whom?

        var blocked = await host.SendAs(CompanyA, new SetUnitStatusCommand(units[0].Id, new UnitStatusInput(UnitStatus.Blocked, "2020-01-01", "lead-7", "Simran")));
        Assert.Equal((UnitStatus.Blocked, PropertyStatus.OnHold, "lead-7"), (blocked.Status, blocked.PropertyStatus, blocked.HoldLeadId));
        var booked = await host.SendAs(CompanyA, new SetUnitStatusCommand(units[1].Id, new UnitStatusInput(UnitStatus.Booked, null, "lead-8", "Aman")));
        Assert.Equal((UnitStatus.Booked, PropertyStatus.TokenReceived), (booked.Status, booked.PropertyStatus));

        await host.RunAsJobAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<HoldRelease>().RunOnceAsync(CancellationToken.None)));
        var board = await host.SendAs(CompanyA, new GetProjectUnitsQuery(project.Id, null, null, null));
        Assert.Equal([(UnitStatus.Available, (string?)null), (UnitStatus.Booked, "lead-8")], board.Select(u => (u.Status, u.HoldLeadId)));
        Assert.Single(await host.SendAs(CompanyA, new GetProjectUnitsQuery(project.Id, null, "A", [UnitStatus.Booked])));

        var counts = (await host.SendAs(CompanyA, new GetProjectQuery(project.Id))).Units;
        Assert.Equal((2, 1, 0, 1), (counts.Total, counts.Available, counts.Blocked, counts.Booked));
        // A unit's status is set on the board, not by "still available?".
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new ConfirmPropertyCommand(units[0].Id,
            new ConfirmPropertyInput(PropertyStatus.Sold, null, ConfirmedVia.Call, null))));
    }

    [Fact]
    public async Task The_price_list_prices_units_by_its_cost_sheet_in_versions_and_an_own_price_wins()
    {
        var (project, type) = await ProjectWithType();
        var units = await Generate(project.Id, type.Id, 4, 4, perFloor: 1);
        var unit = await host.SendAs(CompanyA, new SaveUnitCommand(units[0].Id, new UnitInput
        {
            ProjectId = project.Id, UnitTypeId = type.Id, UnitNumber = "A-401", Tower = "A", Floor = 4, Corner = true,
        }));
        Assert.Null(unit.Price);   // no price list yet

        var charges = new[]
        {
            new ChargeInput("PLC (corner)", ChargeKind.PerSqFt, 100m, Applies: ChargeApplies.Corner),
            new ChargeInput("Floor rise", ChargeKind.PerSqFt, 50m, Applies: ChargeApplies.PerFloorAbove, AboveFloor: 2),
            new ChargeInput("EDC / IDC", ChargeKind.Fixed, 200000m, Taxable: false),
            new ChargeInput("Club membership", ChargeKind.Fixed, 100000m, Mandatory: false),
            new ChargeInput("Ground floor garden", ChargeKind.Fixed, 300000m, Applies: ChargeApplies.Floors, FloorFrom: 0, FloorTo: 0),
        };
        var v1 = await host.SendAs(CompanyA, new SavePriceListCommand(project.Id, new PriceListInput([new UnitTypeRateInput(type.Id, RateBasis.PerSqFt, 4000m)], charges,
            "2026-11-01", null)));
        Assert.Equal(1, v1.Version);

        var sheet = await host.SendAs(CompanyA, new GetUnitCostSheetQuery(unit.Id, null));
        Assert.Equal([("Basic price", 5000000m, true), ("PLC (corner)", 125000m, true), ("Floor rise", 125000m, true), ("EDC / IDC", 200000m, true),
            ("Club membership", 100000m, false), ("GST 5%", 262500m, true)], sheet.Lines.Select(l => (l.Name, l.Amount, l.Included)));
        Assert.Equal((5450000m, 262500m, 5712500m, 5712500m), (sheet.Subtotal, sheet.Gst, sheet.Total, sheet.Payable));
        Assert.Equal((900m, 5556m, 6347m), (sheet.CarpetAreaSqFt!.Value, sheet.BasicPerCarpetSqFt!.Value, sheet.TotalPerCarpetSqFt!.Value));   // RERA: per sq ft of carpet
        Assert.Equal(5712500m, (await host.SendAs(CompanyA, new GetPropertyQuery(unit.Id))).Price);

        // A new version re-prices the units; the old one is kept.
        await host.SendAs(CompanyA, new SavePriceListCommand(project.Id, new PriceListInput([new UnitTypeRateInput(type.Id, RateBasis.PerSqFt, 4400m)], charges, null, null)));
        Assert.Equal([2, 1], (await host.SendAs(CompanyA, new GetPriceListVersionsQuery(project.Id))).Select(l => l.Version));
        Assert.Equal(6237500m, (await host.SendAs(CompanyA, new GetPropertyQuery(unit.Id))).Price);   // 5,500,000 + 125,000 ×2 + 200,000 + 5% on 5,750,000

        // The type's own GST % wins over the project's; an occupancy certificate takes GST off unsold units.
        var typed = ThreeBhk();
        await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, type.Id, new UnitTypeInput
        {
            Name = typed.Name, Type = typed.Type, Bhk = 3, SuperArea = typed.SuperArea, CarpetArea = typed.CarpetArea, GstPercent = 1m,
        }));
        Assert.Equal("GST 1%", (await host.SendAs(CompanyA, new GetUnitCostSheetQuery(unit.Id, null))).Lines.Last().Name);
        var withOc = Own();
        await host.SendAs(CompanyA, new SaveProjectCommand(project.Id, new ProjectInput
        {
            Name = withOc.Name, Kind = withOc.Kind, Locality = withOc.Locality, Towers = withOc.Towers, GstPercent = 5m, OcReceivedOn = "2026-11-01",
            Reras = withOc.Reras,
        }));
        var afterOc = await host.SendAs(CompanyA, new GetUnitCostSheetQuery(unit.Id, null));
        Assert.Equal((0m, 5950000m), (afterOc.Gst, afterOc.Total));

        // Its own price: not re-priced by a new version; the cost sheet still shows the list's lines.
        await host.SendAs(CompanyA, new SaveUnitCommand(unit.Id, new UnitInput
        {
            ProjectId = project.Id, UnitTypeId = type.Id, UnitNumber = "A-401", Tower = "A", Floor = 4, Corner = true, OwnPrice = 6000000m,
        }));
        await host.SendAs(CompanyA, new SavePriceListCommand(project.Id, new PriceListInput([new UnitTypeRateInput(type.Id, RateBasis.PerSqFt, 5000m)], charges, null, null)));
        var own = await host.SendAs(CompanyA, new GetUnitCostSheetQuery(unit.Id, null));
        Assert.Equal((6000000m, 6000000m, true), (own.OwnPrice!.Value, own.Payable!.Value, (await host.SendAs(CompanyA, new GetPropertyQuery(unit.Id))).OwnPrice));
        Assert.Equal(6000000m, (await host.SendAs(CompanyA, new GetPropertyQuery(unit.Id))).Price);

        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SavePriceListCommand(project.Id,
            new PriceListInput([new UnitTypeRateInput(type.Id, RateBasis.PerSqFt, 4000m, AreaBasis.Plot)], null, null, null))));   // no plot area on a flat
    }

    [Fact]
    public async Task A_payment_plan_schedules_the_price_and_warns_past_ten_percent_before_the_agreement()
    {
        var (project, type) = await ProjectWithType();
        var units = await Generate(project.Id, type.Id, 4, 4, perFloor: 1);
        await host.SendAs(CompanyA, new SavePriceListCommand(project.Id, new PriceListInput([new UnitTypeRateInput(type.Id, RateBasis.LumpSum, 5000000m)], null, null, null)));
        var withMilestones = await host.SendAs(CompanyA, new SaveMilestonesCommand(project.Id,
            [new MilestoneInput(null, "Foundation", MilestoneStatus.Done, "2026-06-01"), new MilestoneInput(null, "Slab 5", MilestoneStatus.Pending, null)]));
        var slab = withMilestones.Milestones[1].Id;

        var clp = await host.SendAs(CompanyA, new SavePaymentPlanCommand(project.Id, null, new PaymentPlanInput("Construction linked", PaymentPlanKind.ConstructionLinked,
        [
            new PlanStepInput("Booking amount", null, 200000m, StepDue.OnBooking, null, null),
            new PlanStepInput("Within 30 days", 10m, null, StepDue.DaysAfterBooking, 30, null),
            new PlanStepInput("On agreement for sale", 20m, null, StepDue.OnAgreement, null, null),
            new PlanStepInput("On slab 5", 40m, null, StepDue.AtMilestone, null, slab),
            new PlanStepInput("On possession", 30m, null, StepDue.OnPossession, null, null),
        ], null)));
        Assert.Equal("Slab 5", clp.Steps[3].MilestoneName);
        Assert.Contains("fixed amounts", Assert.Single(clp.Warnings));

        // 5,250,000 with 5% GST: 2,00,000 fixed, then the percents of the 50,50,000 left.
        var sheet = await host.SendAs(CompanyA, new GetUnitCostSheetQuery(units[0].Id, clp.Id));
        Assert.Equal(5250000m, sheet.Payable);
        Assert.Equal([200000m, 505000m, 1010000m, 2020000m, 1515000m], sheet.Schedule!.Steps.Select(s => s.Amount));
        Assert.Equal(5250000m, sheet.Schedule.Total);
        Assert.Contains("more than 10%", Assert.Single(sheet.Schedule.Warnings));   // 7,05,000 before the agreement

        var heavy = await host.SendAs(CompanyA, new SavePaymentPlanCommand(project.Id, null, new PaymentPlanInput("Down payment", PaymentPlanKind.DownPayment,
            [new PlanStepInput("On booking", 15m, null, StepDue.OnBooking, null, null), new PlanStepInput("On possession", 85m, null, StepDue.OnPossession, null, null)], null)));
        Assert.Contains("RERA allows at most 10%", Assert.Single(heavy.Warnings));

        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SavePaymentPlanCommand(project.Id, null, new PaymentPlanInput("Bad",
            PaymentPlanKind.Flexi, [new PlanStepInput("Half", 50m, null, StepDue.OnBooking, null, null)], null))));   // 50%, not 100%
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveMilestonesCommand(project.Id,
            [new MilestoneInput(withMilestones.Milestones[0].Id, "Foundation", MilestoneStatus.Done, null)])));   // slab 5 is in a plan
        Assert.True(await host.SendAs(CompanyA, new DeletePaymentPlanCommand(heavy.Id)));
        Assert.Single((await host.SendAs(CompanyA, new GetProjectQuery(project.Id))).PaymentPlans);
    }

    [Fact]
    public async Task A_requirement_gets_one_match_per_unit_type_that_moves_from_unit_to_unit()
    {
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, Wants3Bhk()));
        var (project, type) = await ProjectWithType();
        host.Bells.Sent.Clear();

        var units = await Generate(project.Id, type.Id, 1, 25);   // 50 units
        Assert.Equal(50, units.Count);
        var match = Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));
        Assert.Equal((project.Id, "Aetro Heights", type.Id, "3 BHK Type A", 50, MatchStatus.New),
            (match.ProjectId, match.ProjectName, match.UnitTypeId, match.UnitTypeName, match.AvailableInGroup, match.Status));
        var bell = Assert.Single(host.Bells.Sent);
        Assert.Equal("3 BHK Type A in Aetro Heights fits Rahul", bell.Notification.Title);

        // The unit it points at is blocked: the match moves to the next one, still new, and nobody is told again.
        var first = match.PropertyId;
        await host.SendAs(CompanyA, new SetUnitStatusCommand(first, new UnitStatusInput(UnitStatus.Blocked, "2030-01-01", "lead-9", null)));
        match = Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));
        Assert.NotEqual(first, match.PropertyId);
        Assert.Equal((49, MatchStatus.New), (match.AvailableInGroup!.Value, match.Status));

        // Shown, then its unit is sold: it follows the type, stays shown, is not offered again.
        await host.SendAs(CompanyA, new UpdateMatchCommand(match.Id, new MatchUpdateInput(MatchStatus.Shown, null)));
        var shownUnit = match.PropertyId;
        await host.SendAs(CompanyA, new SetUnitStatusCommand(shownUnit, new UnitStatusInput(UnitStatus.Sold, null, null, null)));
        match = Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));
        Assert.Equal(MatchStatus.Shown, match.Status);
        Assert.NotEqual(shownUnit, match.PropertyId);
        Assert.Single(host.Bells.Sent);

        // A corner-only customer: one unit fits; when it is blocked none is left and the new match drops.
        var corner = await host.SendAs(CompanyA, new SaveUnitCommand(units[10].Id, new UnitInput
        {
            ProjectId = project.Id, UnitTypeId = type.Id, UnitNumber = units[10].UnitNumber!, Tower = "A", Floor = units[10].Floor, Corner = true,
        }));
        var picky = await host.SendAs(CompanyA, new SaveRequirementCommand(null, Wants3Bhk("lead-2", "Simran", cornerOnly: true)));
        var pickyMatch = Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, picky.Id, null)));
        Assert.Equal((corner.Id, 1), (pickyMatch.PropertyId, pickyMatch.AvailableInGroup!.Value));
        await host.SendAs(CompanyA, new SetUnitStatusCommand(corner.Id, new UnitStatusInput(UnitStatus.Blocked, "2030-01-01", "lead-9", null)));
        Assert.Empty(await host.SendAs(CompanyA, new GetMatchesQuery(null, picky.Id, null)));

        // The dealer's own property beside it is matched as before.
        await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, Locality = "Sector 82", Bhk = 3, Price = 7000000m,
        }));
        Assert.Equal(2, (await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null))).Count);
    }

    [Fact]
    public async Task Project_units_stay_out_of_the_property_list_and_the_dealers_own_counts()
    {
        var (project, type) = await ProjectWithType();
        await Generate(project.Id, type.Id, 1, 2);
        await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput { Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, Locality = "Sector 70" }));

        Assert.Equal(1, (await host.SendAs(CompanyA, new GetPropertiesQuery(new PropertySearchInput()))).TotalCount);
        Assert.Equal(4, (await host.SendAs(CompanyA, new GetPropertiesQuery(new PropertySearchInput { ProjectId = project.Id }))).TotalCount);
        Assert.Equal(5, (await host.SendAs(CompanyA, new GetPropertiesQuery(new PropertySearchInput { IncludeProjectUnits = true }))).TotalCount);

        var dashboard = await host.SendAs(CompanyA, new GetEstateDashboardQuery());
        Assert.Equal((1, 1), (dashboard.AvailableForSale, dashboard.AddedThisWeek));
        var summary = Assert.Single(dashboard.Projects);
        Assert.Equal(("Aetro Heights", 4, 4), (summary.Name, summary.Units.Total, summary.Units.Available));
    }

    [Fact]
    public async Task A_partner_project_is_confirmed_as_a_whole_and_is_not_matched_while_stale()
    {
        var (project, type) = await ProjectWithType(new ProjectInput
        {
            Name = "Green Valley", Kind = ProjectKind.Partner, Locality = "Sector 82", BuilderName = "GV Builders", Stage = ProjectStage.NewLaunch,
        });
        var units = await Generate(project.Id, type.Id, 1, 2, tower: "T1");   // a project without towers takes any tower name
        Assert.Equal(4, units.Count);
        var early = await host.SendAs(CompanyA, new SaveRequirementCommand(null, Wants3Bhk()));
        Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, early.Id, null)));

        await host.WithAsync(CompanyA, async sp =>
        {
            var repo = sp.GetRequiredService<IProjectRepository>();
            var doc = await repo.GetDocument(project.Id).FirstAsync();
            doc.LastConfirmedAt = DateTime.UtcNow.AddDays(-40);
            await repo.UpdateAsync(doc);
        });
        Assert.True((await host.SendAs(CompanyA, new GetProjectQuery(project.Id))).Stale);
        Assert.False((await host.SendAs(CompanyA, new GetPropertyQuery(units[0].Id))).Stale);   // never stale on its own
        Assert.Empty(await host.SendAs(CompanyA, new GetStalePropertiesQuery()));
        var late = await host.SendAs(CompanyA, new SaveRequirementCommand(null, Wants3Bhk("lead-2", "Simran")));
        Assert.Equal(0, late.NewMatches);   // not sent while the builder's inventory is unchecked

        var confirmed = await host.SendAs(CompanyA, new ConfirmProjectCommand(project.Id));
        Assert.False(confirmed.Stale);
        Assert.True((await host.SendAs(CompanyA, new GetPropertyQuery(units[3].Id))).LastConfirmedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, late.Id, null)));

        var own = await host.SendAs(CompanyA, new SaveProjectCommand(null, Own("Own One")));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new ConfirmProjectCommand(own.Id)));
    }

    [Fact]
    public async Task The_shared_project_page_shows_the_project_never_unit_numbers_cp_terms_or_the_builder()
    {
        var partner = Partner();
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = partner.Name, Kind = ProjectKind.Partner, Locality = "Sector 82", BuilderName = partner.BuilderName, BuilderPhone = partner.BuilderPhone,
            CpTerms = partner.CpTerms, Overview = "Homes beside the golf course", Brochure = new MediaFileInput("real-estate/b.pdf", "https://cdn.test/b.pdf", "Brochure"),
        }));
        var type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, ThreeBhk()));
        var villa = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput
        {
            Name = "Villa", Type = PropertyType.Villa, Bhk = 5, PlotArea = new AreaInput(500, AreaUnit.SqYd), PriceOnRequest = true,
        }));
        await Generate(project.Id, type.Id, 1, 1, tower: "A");
        await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput { ProjectId = project.Id, UnitTypeId = villa.Id, Count = 2, Pattern = "V-{n}" }));
        await host.SendAs(CompanyA, new SavePriceListCommand(project.Id, new PriceListInput(
            [new UnitTypeRateInput(type.Id, RateBasis.PerSqFt, 4000m), new UnitTypeRateInput(villa.Id, RateBasis.LumpSum, 30000000m)], null, null, null)));
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, Wants3Bhk()));

        var share = await host.SendAs(CompanyA, new CreateProjectShareCommand(project.Id, requirement.Id));
        Assert.StartsWith("https://app.zeronova.in/pj/", share.Url);
        Assert.Contains("From ₹50,00,000", share.Message);   // the villa's price is on request
        Assert.Equal(MatchStatus.Shown, Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null))).Status);

        var page = await Public(new GetSharedProjectQuery(share.Token));
        await Public(new GetSharedProjectQuery(share.Token));
        Assert.Equal(("Green Valley", "Brochure", "Sharma Properties", true), (page.Name, page.Brochure!.Name, page.DealerName, page.Active));
        Assert.Equal([("3 BHK Type A", (decimal?)5000000m, 2), ("Villa", (decimal?)null, 2)], page.UnitTypes.Select(t => (t.Name, t.FromPrice, t.Available)));
        var json = JsonSerializer.Serialize(page);
        foreach (var secret in new[] { "A-101", "V-1", "919812300000", "On registry", "GV Builders" })
            Assert.DoesNotContain(secret, json);
        Assert.Equal(2, Assert.Single(await host.SendAs(CompanyA, new GetProjectSharesQuery(project.Id))).Opens);

        // A unit shared as a property: no unit number, and a price on request stays hidden.
        var villaUnit = (await host.SendAs(CompanyA, new GetProjectUnitsQuery(project.Id, villa.Id, null, null)))[0];
        var unitShare = await host.SendAs(CompanyA, new CreateShareCommand(villaUnit.Id, null));
        var unitPage = await Public(new GetSharedPropertyQuery(unitShare.Token));
        Assert.Null(unitPage.Price);
        Assert.DoesNotContain("V-1", JsonSerializer.Serialize(unitPage));
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new GetSharedPropertyQuery(share.Token)));   // a project link is not a property link
    }

    [Fact]
    public async Task An_own_project_is_shared_only_once_it_is_rera_registered_or_needs_no_registration()
    {
        var unregistered = new ProjectInput { Name = "Farm Estates", Kind = ProjectKind.Own, Locality = "Sector 82", Categories = [ProjectCategory.Farmhouse] };
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, unregistered));
        Assert.Contains("RERA", (await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new CreateProjectShareCommand(project.Id, null)))).Message);

        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveProjectCommand(project.Id, new ProjectInput
        {
            Name = "Farm Estates", Kind = ProjectKind.Own, Locality = "Sector 82", ReraNotRequired = true,
        })));   // say why
        await host.SendAs(CompanyA, new SaveProjectCommand(project.Id, new ProjectInput
        {
            Name = "Farm Estates", Kind = ProjectKind.Own, Locality = "Sector 82", ReraNotRequired = true, ReraExemptReason = "Land under 500 sq m",
        }));
        Assert.NotNull(await host.SendAs(CompanyA, new CreateProjectShareCommand(project.Id, null)));
        var registered = await host.SendAs(CompanyA, new SaveProjectCommand(null, Own()));
        Assert.NotNull(await host.SendAs(CompanyA, new CreateProjectShareCommand(registered.Id, null)));
    }

    [Fact]
    public async Task Booked_or_sold_units_keep_a_project_and_a_type_with_units_stays()
    {
        var (project, type) = await ProjectWithType();
        var units = await Generate(project.Id, type.Id, 1, 1);
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, Wants3Bhk()));
        Assert.Equal(1, requirement.NewMatches);

        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new DeleteUnitTypeCommand(type.Id)));
        var empty = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput { Name = "Studio", Type = PropertyType.Flat, Bhk = 1 }));
        Assert.True(await host.SendAs(CompanyA, new DeleteUnitTypeCommand(empty.Id)));

        await host.SendAs(CompanyA, new SetUnitStatusCommand(units[0].Id, new UnitStatusInput(UnitStatus.Booked, null, "lead-5", "Aman")));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new DeleteProjectCommand(project.Id)));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new DeletePropertyCommand(units[0].Id)));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-1", [EstatePermissions.PropertyManage],
            new DeletePropertyCommand(units[1].Id)));   // removing a unit needs PPJM

        await host.SendAs(CompanyA, new SetUnitStatusCommand(units[0].Id, new UnitStatusInput(UnitStatus.Available, null, null, null)));
        Assert.True(await host.SendAs(CompanyA, new DeleteProjectCommand(project.Id)));
        Assert.Equal(0, (await host.SendAs(CompanyA, new GetPropertiesQuery(new PropertySearchInput { IncludeProjectUnits = true }))).TotalCount);
        Assert.Empty(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new GetProjectQuery(project.Id)));
    }

    [Fact]
    public async Task The_indexes_that_refuse_a_second_unit_number_and_a_second_type_match_exist()
    {
        var names = new List<string>();
        await host.WithAsync(CompanyA, async sp =>
        {
            var db = sp.GetRequiredService<MongoDB.Driver.IMongoDatabase>();
            foreach (var collection in new[] { "re_properties", "re_matches", "re_price_lists", "re_projects" })
                names.AddRange(MongoDB.Driver.IAsyncCursorExtensions.ToList(await db.GetCollection<MongoDB.Bson.BsonDocument>(collection).Indexes.ListAsync())
                    .Select(i => i["name"].AsString));
        });
        Assert.Contains("company_project_unit_unique", names);
        Assert.Contains("company_requirement_group_unique", names);
        Assert.Contains("company_project_version_unique", names);
    }

    [Fact]
    public async Task A_site_visit_can_be_to_a_project_with_its_unit_optional()
    {
        var (project, type) = await ProjectWithType();
        var units = await Generate(project.Id, type.Id, 1, 1);
        var requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, Wants3Bhk()));
        var at = DateTimeOffset.UtcNow.AddDays(2);

        var visit = await host.SendAs(CompanyA, new ScheduleVisitCommand(new VisitInput
        {
            ProjectId = project.Id, RequirementId = requirement.Id, LeadId = "lead-1", LeadName = "Rahul", At = at,
        }));
        Assert.Equal(((string?)null, project.Id, "Aetro Heights", "PJ-0001"), (visit.PropertyId, visit.ProjectId, visit.PropertyTitle, visit.PropertyCode));
        Assert.Contains("your visit to see Aetro Heights", visit.ClientMessage);
        Assert.Single(await host.SendAs(CompanyA, new GetVisitsQuery(null, null, null, null, null, project.Id)));

        var unitVisit = await host.SendAs(CompanyA, new ScheduleVisitCommand(new VisitInput { PropertyId = units[0].Id, LeadId = "lead-1", At = at }));
        Assert.Equal((units[0].Id, project.Id), (unitVisit.PropertyId, unitVisit.ProjectId));
        var other = await host.SendAs(CompanyA, new SaveProjectCommand(null, Own("Other")));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new ScheduleVisitCommand(new VisitInput
        {
            PropertyId = units[0].Id, ProjectId = other.Id, LeadId = "lead-1", At = at,
        })));

        await host.SendAs(CompanyA, new RecordVisitResultCommand(visit.Id, new VisitResultInput(VisitStatus.Done, VisitOutcome.NotInterested, "Too far")));
        var match = Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(null, requirement.Id, null)));
        Assert.Equal((MatchStatus.Rejected, "Too far"), (match.Status, match.RejectReason));
    }
}
