using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Projects;

namespace ZeroNovaRealEstate.Application.Features.Projects;

// ── Price list ────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Every version of a project's price list, newest first (D22: old versions are kept).</summary>
public sealed record GetPriceListVersionsQuery(string ProjectId) : IRequest<IReadOnlyList<PriceListDto>>;

public sealed class GetPriceListVersionsHandler(IProjectRepository projects, IUnitTypeRepository unitTypes, IPriceListRepository priceLists)
    : IRequestHandler<GetPriceListVersionsQuery, IReadOnlyList<PriceListDto>>
{
    public async Task<IReadOnlyList<PriceListDto>> Handle(GetPriceListVersionsQuery request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        var names = (await unitTypes.GetDocuments(t => t.ProjectId == project.Id, true).ToListAsync(ct)).ToDictionary(t => t.Id, t => t.Name);
        var rows = await priceLists.GetDocuments(l => l.ProjectId == project.Id).OrderByDescending(l => l.ListVersion).Take(200).ToListAsync(ct);
        return [.. rows.Select(l => ProjectMapper.PriceList(l, names))];
    }
}

/// <summary>
/// A new version of the project's price list (D22). It prices every unit without its own price (booked and sold units
/// keep theirs), and the unit types are matched again (a price change can bring a customer in or out of budget).
/// </summary>
public sealed record SavePriceListCommand(string ProjectId, PriceListInput Input) : IRequest<PriceListDto>;

public sealed class SavePriceListHandler(IProjectRepository projects, IUnitTypeRepository unitTypes, IPriceListRepository priceLists, ProjectUnitSync sync,
    TimeProvider clock)
    : IRequestHandler<SavePriceListCommand, PriceListDto>
{
    public async Task<PriceListDto> Handle(SavePriceListCommand request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        var input = request.Input;
        var types = (await unitTypes.GetDocuments(t => t.ProjectId == project.Id).ToListAsync(ct)).ToDictionary(t => t.Id);

        var rates = new List<UnitTypeRateModel>();
        foreach (var r in input.Rates.GroupBy(r => r.UnitTypeId).Select(g => g.Last()))
        {
            var type = types.GetValueOrDefault(r.UnitTypeId) ?? throw new ValidatorException("A rate names a unit type that is not this project's.");
            if (r.Rate <= 0 || r.Rate > 100_000_000_000m)
                throw new ValidatorException($"{type.Name}: the rate must be more than zero.");
            if (r.Basis != RateBasis.LumpSum && CostSheetEngine.AreaOf(type, r.Area) is null)
                throw new ValidatorException($"{type.Name} has no {r.Area.ToString().ToLowerInvariant()} area to price on; fill it on the unit type or pick another area.");
            rates.Add(new UnitTypeRateModel { UnitTypeId = r.UnitTypeId, Basis = r.Basis, Rate = r.Rate, Area = r.Area });
        }

        var charges = new List<ChargeModel>();
        foreach (var c in input.Charges ?? [])
        {
            var name = EstateText.Clean(c.Name) ?? throw new ValidatorException("Name every charge (PLC, floor rise, EDC/IDC, parking …).");
            if (c.Amount < 0 || (c.Kind == ChargeKind.PercentOfBase && c.Amount > 100))
                throw new ValidatorException($"{name}: the amount must be zero or more (a percent up to 100).");
            if (c.Applies == ChargeApplies.Floors && (c.FloorFrom is null || c.FloorTo is null || c.FloorFrom > c.FloorTo))
                throw new ValidatorException($"{name}: give the floors from and to.");
            if (c.Applies == ChargeApplies.PerFloorAbove && c.AboveFloor is null)
                throw new ValidatorException($"{name}: say the floor above which it is charged.");
            var ids = (c.UnitTypeIds ?? []).Distinct().ToList();
            if (ids.Any(id => !types.ContainsKey(id)))
                throw new ValidatorException($"{name} names a unit type that is not this project's.");
            charges.Add(new ChargeModel
            {
                Name = name, Kind = c.Kind, Amount = c.Amount, Mandatory = c.Mandatory, Applies = c.Applies,
                FloorFrom = c.Applies == ChargeApplies.Floors ? c.FloorFrom : null, FloorTo = c.Applies == ChargeApplies.Floors ? c.FloorTo : null,
                AboveFloor = c.Applies == ChargeApplies.PerFloorAbove ? c.AboveFloor : null, Taxable = c.Taxable, UnitTypeIds = ids,
            });
        }
        if (charges.Count > 60)
            throw new ValidatorException("Up to 60 charges.");

        var latest = await priceLists.GetDocuments(l => l.ProjectId == project.Id).OrderByDescending(l => l.ListVersion).Select(l => l.ListVersion).FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var list = new PriceListDocument
        {
            Id = priceLists.GetNewId, ProjectId = project.Id, ListVersion = latest + 1,
            EffectiveFrom = EstateText.Date(input.EffectiveFrom, "Effective from") ?? EstateText.Today(clock), Rates = rates, Charges = charges,
            Notes = EstateText.Clean(input.Notes), CreatedAt = now,
        };
        await priceLists.AddAsync(list, ct);
        await sync.RefreshAsync(project, null, ct);
        return ProjectMapper.PriceList(list, types.ToDictionary(t => t.Key, t => t.Value.Name));
    }
}

/// <summary>A unit's cost sheet from the current price list (D22) and, with a plan, its payment schedule (D23).</summary>
public sealed record GetUnitCostSheetQuery(string PropertyId, string? PaymentPlanId) : IRequest<CostSheetDto>;

public sealed class GetUnitCostSheetHandler(IProjectRepository projects, IUnitTypeRepository unitTypes, IPropertyRepository properties, IPaymentPlanRepository plans,
    ProjectUnitSync sync)
    : IRequestHandler<GetUnitCostSheetQuery, CostSheetDto>
{
    public async Task<CostSheetDto> Handle(GetUnitCostSheetQuery request, CancellationToken ct)
    {
        var unit = await ProjectLookup.RequireUnitAsync(properties, request.PropertyId, ct);
        var project = await ProjectLookup.RequireAsync(projects, unit.ProjectId!, ct);
        var type = await ProjectLookup.RequireTypeAsync(unitTypes, unit.UnitTypeId!, ct);
        var list = await sync.CurrentListAsync(project.Id, ct);
        var sheet = CostSheetEngine.Compute(unit, type, list, ProjectUnits.GstFor(project, type, unit));
        var payable = unit.OwnPrice ? unit.Price : sheet?.Total;

        PaymentScheduleDto? schedule = null;
        if (EstateText.Clean(request.PaymentPlanId) is { } planId)
        {
            var plan = await plans.GetDocument(planId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Payment plan not found.");
            if (plan.ProjectId != project.Id)
                throw new ValidatorException("This payment plan is another project's.");
            schedule = payable is { } price ? CostSheetEngine.Schedule(plan, price, project.Milestones)
                : throw new ValidatorException("The unit has no price yet: add its rate to the price list, or give it its own price.");
        }

        // RERA prices on carpet area: always say what the unit costs per sq ft of carpet, whatever area the rate is on.
        var carpet = type.CarpetArea?.SqFt;
        decimal? PerCarpet(decimal? amount) => carpet is > 0 && amount is { } a ? CostSheetEngine.Rupee(a / carpet.Value) : null;

        return new CostSheetDto(unit.Id, unit.Code, unit.UnitNumber, unit.UnitTypeName, sheet is null ? null : list?.Id, sheet is null ? null : list?.ListVersion,
            sheet?.Rate.Basis, sheet?.Rate.Rate, sheet?.Rate.Area, sheet?.AreaSqFt, carpet,
            PerCarpet(sheet?.Lines.FirstOrDefault(l => l.Kind == CostLineKind.Base)?.Amount), PerCarpet(payable),
            sheet?.Lines ?? [], sheet?.Subtotal ?? 0m, sheet?.GstPercent, sheet?.Gst ?? 0m, sheet?.Total ?? 0m, unit.OwnPrice ? unit.Price : null, payable, schedule);
    }
}

// ── Payment plans ─────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Adds (no id) or changes a payment plan (D23): steps with a percent or a fixed amount, due on booking, N days after
/// it, on the agreement for sale, at a milestone of the project, or on possession. Percents are of what is left after the
/// fixed amounts and total 100%. More than 10% before the agreement for sale comes back as a warning (RERA s.13).
/// </summary>
public sealed record SavePaymentPlanCommand(string ProjectId, string? PaymentPlanId, PaymentPlanInput Input) : IRequest<PaymentPlanDto>;

public sealed class SavePaymentPlanHandler(IProjectRepository projects, IPaymentPlanRepository plans) : IRequestHandler<SavePaymentPlanCommand, PaymentPlanDto>
{
    public async Task<PaymentPlanDto> Handle(SavePaymentPlanCommand request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        var input = request.Input;
        var name = EstateText.Clean(input.Name) ?? throw new ValidatorException("Name the payment plan.");
        if (input.Steps.Count is 0 or > 40)
            throw new ValidatorException("A plan has 1 to 40 steps.");

        var steps = new List<PlanStepModel>();
        foreach (var s in input.Steps)
        {
            var label = EstateText.Clean(s.Label) ?? throw new ValidatorException("Name every step (on booking, on slab 3 …).");
            if ((s.Percent is null) == (s.Amount is null))
                throw new ValidatorException($"{label}: give a percent or an amount, not both.");
            if (s.Percent is <= 0 or > 100 || s.Amount is <= 0)
                throw new ValidatorException($"{label}: the percent or amount must be more than zero.");
            if (s.Due == StepDue.DaysAfterBooking && s.Days is null or < 0 or > 3650)
                throw new ValidatorException($"{label}: say how many days after booking.");
            if (s.Due == StepDue.AtMilestone && (s.MilestoneId is null || project.Milestones.All(m => m.Id != s.MilestoneId)))
                throw new ValidatorException($"{label}: pick one of the project's construction milestones.");
            steps.Add(new PlanStepModel
            {
                Label = label, Percent = s.Percent, Amount = s.Amount, Due = s.Due, Days = s.Due == StepDue.DaysAfterBooking ? s.Days : null,
                MilestoneId = s.Due == StepDue.AtMilestone ? s.MilestoneId : null,
            });
        }
        var percent = steps.Sum(s => s.Percent ?? 0m);
        if (steps.All(s => s.Percent is null))
            throw new ValidatorException("A plan needs at least one percent step to take the balance.");
        if (Math.Abs(percent - 100m) > 0.001m)
            throw new ValidatorException($"The percent steps add up to {percent:0.##}%, not 100%.");

        var plan = string.IsNullOrEmpty(request.PaymentPlanId)
            ? new PaymentPlanDocument { Id = plans.GetNewId, ProjectId = project.Id, Name = name }
            : await plans.GetDocument(request.PaymentPlanId!).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Payment plan not found.");
        if (plan.ProjectId != project.Id)
            throw new ValidatorException("This payment plan is another project's.");
        plan.Name = name;
        plan.Kind = input.Kind;
        plan.Steps = steps;
        plan.Notes = EstateText.Clean(input.Notes);
        if (string.IsNullOrEmpty(request.PaymentPlanId))
            await plans.AddAsync(plan, ct);
        else
            await plans.UpdateAsync(plan, ct);
        return ProjectMapper.Plan(plan, project.Milestones);
    }
}

public sealed record DeletePaymentPlanCommand(string PaymentPlanId) : IRequest<bool>;

public sealed class DeletePaymentPlanHandler(IPaymentPlanRepository plans) : IRequestHandler<DeletePaymentPlanCommand, bool>
{
    public async Task<bool> Handle(DeletePaymentPlanCommand request, CancellationToken ct)
    {
        await plans.DeleteAsync(request.PaymentPlanId, ct);
        return true;
    }
}
