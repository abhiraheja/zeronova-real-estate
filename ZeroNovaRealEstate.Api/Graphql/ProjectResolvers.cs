using HotChocolate.Authorization;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Api.Authorization;
using ZeroNovaRealEstate.Api.Graphql.Base;
using ZeroNovaRealEstate.Application.Features.Projects;
using ZeroNovaRealEstate.Models.Projects;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Api.Graphql;

/// <summary>
/// Real Estate projects (design 7, P1): builders' own and channel-partner projects, unit types, the unit board, price
/// lists with versions and the cost sheet, payment plans and milestones, construction updates, and the project share
/// page. Seeing them is PPRV / PPRM (D25); changing them is PPJM; blocking or booking a unit is PPRM.
/// </summary>
[ExtendObjectType(nameof(Query))]
public sealed class ProjectQuery(IMediator mediator) : QueryBase
{
    [RequirePermission(P.PropertyView, P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_estateProjects")]
    public Task<IReadOnlyList<ProjectSummaryDto>> Projects(ProjectSearchInput? search, CancellationToken ct) =>
        mediator.Send(new GetProjectsQuery(search ?? new ProjectSearchInput()), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_estateProject")]
    public Task<ProjectDto> Project(string projectId, CancellationToken ct) => mediator.Send(new GetProjectQuery(projectId), ct);

    /// <summary>The unit board: a project's units by tower and floor, of a type or tower, in some statuses.</summary>
    [RequirePermission(P.PropertyView, P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_estateProjectUnits")]
    public Task<IReadOnlyList<UnitDto>> ProjectUnits(string projectId, string? unitTypeId, string? tower, IReadOnlyList<UnitStatus>? statuses, CancellationToken ct) =>
        mediator.Send(new GetProjectUnitsQuery(projectId, unitTypeId, tower, statuses), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_estateUnitCostSheet")]
    public Task<CostSheetDto> UnitCostSheet(string propertyId, string? paymentPlanId, CancellationToken ct) =>
        mediator.Send(new GetUnitCostSheetQuery(propertyId, paymentPlanId), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_estatePriceListVersions")]
    public Task<IReadOnlyList<PriceListDto>> PriceListVersions(string projectId, CancellationToken ct) => mediator.Send(new GetPriceListVersionsQuery(projectId), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_estateProjectShares")]
    public Task<IReadOnlyList<ProjectShareDto>> ProjectShares(string projectId, CancellationToken ct) => mediator.Send(new GetProjectSharesQuery(projectId), ct);

    /// <summary>The shared project page — anyone with the link.</summary>
    [AllowAnonymous]
    [GraphQLName("ZeroNova_estateSharedProject")]
    public Task<SharedProjectDto> SharedProject(string token, CancellationToken ct) => mediator.Send(new GetSharedProjectQuery(token), ct);
}

[ExtendObjectType(nameof(Mutation))]
public sealed class ProjectMutation(IMediator mediator) : MutationBase
{
    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_saveEstateProject")]
    public Task<ProjectDto> SaveProject(string? projectId, ProjectInput input, CancellationToken ct) => mediator.Send(new SaveProjectCommand(projectId, input), ct);

    /// <summary>Only a project with no booked or sold units.</summary>
    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_deleteEstateProject")]
    public Task<bool> DeleteProject(string projectId, CancellationToken ct) => mediator.Send(new DeleteProjectCommand(projectId), ct);

    /// <summary>A partner project's inventory checked with its builder: every unit fresh again.</summary>
    [RequirePermission(P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_confirmEstateProject")]
    public Task<ProjectDto> ConfirmProject(string projectId, CancellationToken ct) => mediator.Send(new ConfirmProjectCommand(projectId), ct);

    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_saveEstateUnitType")]
    public Task<UnitTypeDto> SaveUnitType(string projectId, string? unitTypeId, UnitTypeInput input, CancellationToken ct) =>
        mediator.Send(new SaveUnitTypeCommand(projectId, unitTypeId, input), ct);

    /// <summary>Only a unit type with no units.</summary>
    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_deleteEstateUnitType")]
    public Task<bool> DeleteUnitType(string unitTypeId, CancellationToken ct) => mediator.Send(new DeleteUnitTypeCommand(unitTypeId), ct);

    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_generateEstateUnits")]
    public Task<IReadOnlyList<UnitDto>> GenerateUnits(GenerateUnitsInput input, CancellationToken ct) => mediator.Send(new GenerateUnitsCommand(input), ct);

    /// <summary>One unit added (no id) or changed: number, tower, floor, its own fields, its own price.</summary>
    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_saveEstateUnit")]
    public Task<UnitDto> SaveUnit(string? propertyId, UnitInput input, CancellationToken ct) => mediator.Send(new SaveUnitCommand(propertyId, input), ct);

    /// <summary>Available, blocked for a lead until a date, booked, sold or withdrawn.</summary>
    [RequirePermission(P.PropertyManage)]
    [GraphQLName("ZeroNova_setEstateUnitStatus")]
    public Task<UnitDto> SetUnitStatus(string propertyId, UnitStatusInput input, CancellationToken ct) => mediator.Send(new SetUnitStatusCommand(propertyId, input), ct);

    /// <summary>A new version of the price list; the units are priced again.</summary>
    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_saveEstatePriceList")]
    public Task<PriceListDto> SavePriceList(string projectId, PriceListInput input, CancellationToken ct) => mediator.Send(new SavePriceListCommand(projectId, input), ct);

    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_saveEstatePaymentPlan")]
    public Task<PaymentPlanDto> SavePaymentPlan(string projectId, string? paymentPlanId, PaymentPlanInput input, CancellationToken ct) =>
        mediator.Send(new SavePaymentPlanCommand(projectId, paymentPlanId, input), ct);

    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_deleteEstatePaymentPlan")]
    public Task<bool> DeletePaymentPlan(string paymentPlanId, CancellationToken ct) => mediator.Send(new DeletePaymentPlanCommand(paymentPlanId), ct);

    /// <summary>The project's construction milestones, as a whole list.</summary>
    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_saveEstateMilestones")]
    public Task<ProjectDto> SaveMilestones(string projectId, IReadOnlyList<MilestoneInput> milestones, CancellationToken ct) =>
        mediator.Send(new SaveMilestonesCommand(projectId, milestones), ct);

    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_addEstateConstructionUpdate")]
    public Task<ProjectDto> AddConstructionUpdate(string projectId, ConstructionUpdateInput input, CancellationToken ct) =>
        mediator.Send(new AddConstructionUpdateCommand(projectId, input), ct);

    [RequirePermission(P.ProjectManage)]
    [GraphQLName("ZeroNova_deleteEstateConstructionUpdate")]
    public Task<ProjectDto> DeleteConstructionUpdate(string projectId, string updateId, CancellationToken ct) =>
        mediator.Send(new DeleteConstructionUpdateCommand(projectId, updateId), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage, P.RequirementManage)]
    [GraphQLName("ZeroNova_shareEstateProject")]
    public Task<ProjectShareDto> ShareProject(string projectId, string? requirementId, CancellationToken ct) =>
        mediator.Send(new CreateProjectShareCommand(projectId, requirementId), ct);
}
