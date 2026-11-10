using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Projects;

/// <summary>Units as builders speak of them (D20), and what a unit takes from its type and project (D18).</summary>
public static class ProjectUnits
{
    public static UnitStatus StatusOf(PropertyStatus status) => status switch
    {
        PropertyStatus.Available => UnitStatus.Available,
        PropertyStatus.OnHold or PropertyStatus.UnderNegotiation => UnitStatus.Blocked,
        PropertyStatus.TokenReceived => UnitStatus.Booked,
        PropertyStatus.Sold or PropertyStatus.Rented => UnitStatus.Sold,
        _ => UnitStatus.Withdrawn,
    };

    public static PropertyStatus PropertyStatusOf(UnitStatus status) => status switch
    {
        UnitStatus.Available => PropertyStatus.Available,
        UnitStatus.Blocked => PropertyStatus.OnHold,
        UnitStatus.Booked => PropertyStatus.TokenReceived,
        UnitStatus.Sold => PropertyStatus.Sold,
        _ => PropertyStatus.Withdrawn,
    };

    /// <summary>Booked or sold: a unit a customer has paid for, which nothing may remove.</summary>
    public static bool IsTaken(PropertyStatus status) => status is PropertyStatus.TokenReceived or PropertyStatus.Sold or PropertyStatus.Rented;

    public static UnitCountsDto Count(IEnumerable<PropertyStatus> statuses)
    {
        var all = statuses.Select(StatusOf).ToList();
        return new UnitCountsDto(all.Count, all.Count(s => s == UnitStatus.Available), all.Count(s => s == UnitStatus.Blocked),
            all.Count(s => s == UnitStatus.Booked), all.Count(s => s == UnitStatus.Sold), all.Count(s => s == UnitStatus.Withdrawn));
    }

    public static bool IsCommercial(PropertyType type) => type is PropertyType.Shop or PropertyType.Office or PropertyType.Showroom or PropertyType.Warehouse;

    /// <summary>
    /// The type's fields (type, BHK, bathrooms, balconies, areas, dimensions) and the project's (city, locality, society =
    /// project name, map pin, amenities, construction, possession, photos) onto a unit (D18). Its own fields — number,
    /// tower, floor, facing, corner, park facing, road width, notes, own price — are left as they are.
    /// </summary>
    public static void Inherit(PropertyDocument u, ProjectDocument project, UnitTypeDocument type)
    {
        var built = MatchEngine.IsBuilt(type.Type);
        var commercial = IsCommercial(type.Type);
        var partner = project.Kind == ProjectKind.Partner;

        u.ProjectId = project.Id;
        u.UnitTypeId = type.Id;
        u.UnitTypeName = type.Name;
        u.Purpose = PropertyPurpose.Sale;
        u.Type = type.Type;

        u.Bhk = built && !commercial ? type.Bhk : null;
        u.Bathrooms = built ? type.Bathrooms : null;
        u.Balconies = built && !commercial ? type.Balconies : null;
        u.PlotArea = type.PlotArea;
        u.BuiltUpArea = built ? type.BuiltUpArea : null;
        u.CarpetArea = built ? type.CarpetArea : null;
        u.SuperArea = built ? type.SuperArea : null;
        u.SizeSqFt = (u.SuperArea ?? u.BuiltUpArea ?? u.CarpetArea ?? u.PlotArea)?.SqFt;
        u.FrontFeet = type.FrontFeet;
        u.DepthFeet = type.DepthFeet;

        u.City = project.City;
        u.State = project.State;
        u.Locality = project.Locality;
        u.LocalityKey = project.LocalityKey;
        u.Society = project.Name;
        u.Latitude = project.Latitude;
        u.Longitude = project.Longitude;
        u.Amenities = [.. project.Amenities];
        u.Construction = built ? project.Stage is ProjectStage.ReadyToMove or ProjectStage.Completed ? ConstructionStatus.Ready : ConstructionStatus.UnderConstruction : null;
        u.PossessionDate = project.PossessionDate;
        if (!built)
            u.Floor = null;
        u.TotalFloors = built ? project.Towers.FirstOrDefault(t => string.Equals(t.Name, u.Tower, StringComparison.OrdinalIgnoreCase))?.Floors : null;

        u.Source = partner ? PropertySource.OtherDealer : PropertySource.Own;
        u.SourceDealerName = partner ? project.BuilderName : null;
        u.SourceDealerPhone = partner ? project.BuilderPhone : null;
        u.Mandate = MandateKind.Open;

        u.Photos = [.. project.Photos.Take(30), .. type.FloorPlan is { } plan ? new[] { plan } : []];
        u.VideoUrl = project.VideoUrl;

        u.Title = $"{type.Name} in {project.Name}";
        u.SearchText = string.Join(' ', new[] { u.Title, project.Code, project.Locality, project.City, u.Tower, u.UnitNumber, u.Code, u.Notes }
            .Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();
    }

    /// <summary>A unit on the board; whom it is held for only for <paramref name="scope"/> with PRQV or the member who held it (null: nobody).</summary>
    public static UnitDto ToDto(PropertyDocument u, EstateScope? scope)
    {
        var seesHold = scope is not null && MaySeeHold(u, scope);
        return new(u.Id, u.Code, u.ProjectId ?? string.Empty, u.UnitTypeId ?? string.Empty, u.UnitTypeName, u.Tower, u.Floor, u.UnitNumber, StatusOf(u.Status),
            u.Status, u.HoldUntil, seesHold ? u.HoldLeadId : null, seesHold ? u.HoldLeadName : null, u.Price, u.OwnPrice, u.SizeSqFt, u.Facing, u.Corner,
            u.ParkFacing, u.RoadWidthFeet, u.Notes);
    }

    /// <summary>The lead a unit (or property) is held for is the hold-maker's and the managers' (PRQV) — not every member's.</summary>
    public static bool MaySeeHold(PropertyDocument u, EstateScope scope) =>
        scope.SeesAllRequirements || (u.HoldBy is { } by && by == scope.UserIdOrNull);

    /// <summary>
    /// The GST % on a unit's cost sheet: none for an unsold unit once the project has its occupancy certificate; else the
    /// unit type's, else the project's (typed in, never assumed).
    /// </summary>
    public static decimal? GstFor(ProjectDocument project, UnitTypeDocument type, PropertyDocument unit) =>
        project.OcReceivedOn is not null && !IsTaken(unit.Status) ? 0m : type.GstPercent ?? project.GstPercent;

    /// <summary>A unit number's comparison key: "A-101" and "a - 101" are one (D19).</summary>
    public static string NumberKey(string number) => new string(number.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}

/// <summary>The result of pricing one unit from a price list (D22).</summary>
public sealed record CostSheetResult(UnitTypeRateModel Rate, decimal? AreaSqFt, IReadOnlyList<CostLineDto> Lines, decimal Subtotal, decimal? GstPercent, decimal Gst,
    decimal Total);

/// <summary>
/// The cost sheet (D22): the base price (rate per sq ft or sq yd of the chosen area, or a lump sum), each charge that
/// applies to the unit (fixed, per area, % of base; corner, park facing, floors, per floor above N), GST on the taxable
/// lines at the project's typed-in rate. Every line is in whole rupees; optional charges are shown, not added.
/// </summary>
public static class CostSheetEngine
{
    public static decimal Rupee(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    public static AreaModel? AreaOf(UnitTypeDocument type, AreaBasis basis) => basis switch
    {
        AreaBasis.Super => type.SuperArea,
        AreaBasis.BuiltUp => type.BuiltUpArea,
        AreaBasis.Carpet => type.CarpetArea,
        _ => type.PlotArea,
    };

    /// <summary>Null when the list has no rate for the unit's type (or the type lacks the rate's area).</summary>
    public static CostSheetResult? Compute(PropertyDocument unit, UnitTypeDocument type, PriceListDocument? list, decimal? gstPercent)
    {
        var rate = list?.Rates.FirstOrDefault(r => r.UnitTypeId == type.Id);
        if (list is null || rate is null)
            return null;
        var area = AreaOf(type, rate.Area);
        if (rate.Basis != RateBasis.LumpSum && area is null)
            return null;

        var sqFt = area?.SqFt;
        var basePrice = Rupee(rate.Basis switch
        {
            RateBasis.PerSqFt => rate.Rate * sqFt!.Value,
            RateBasis.PerSqYd => rate.Rate * sqFt!.Value / 9m,
            _ => rate.Rate,
        });
        var lines = new List<CostLineDto> { new("Basic price", CostLineKind.Base, BaseDetail(rate, area), basePrice, true, true, true) };

        var chargeSqFt = sqFt ?? unit.SizeSqFt ?? 0m;
        foreach (var charge in list.Charges.Where(c => c.UnitTypeIds.Count == 0 || c.UnitTypeIds.Contains(type.Id)))
        {
            var times = Times(charge, unit);
            if (times <= 0)
                continue;
            var one = charge.Kind switch
            {
                ChargeKind.Fixed => charge.Amount,
                ChargeKind.PerSqFt => charge.Amount * chargeSqFt,
                ChargeKind.PerSqYd => charge.Amount * chargeSqFt / 9m,
                _ => basePrice * charge.Amount / 100m,
            };
            lines.Add(new CostLineDto(charge.Name, CostLineKind.Charge, ChargeDetail(charge, times, chargeSqFt), Rupee(one * times), charge.Taxable,
                charge.Mandatory, charge.Mandatory));
        }

        var included = lines.Where(l => l.Included).ToList();
        var subtotal = included.Sum(l => l.Amount);
        var taxable = included.Where(l => l.Taxable).Sum(l => l.Amount);
        var gst = gstPercent is > 0 ? Rupee(taxable * gstPercent.Value / 100m) : 0m;
        if (gst > 0)
            lines.Add(new CostLineDto($"GST {gstPercent!.Value.ToString("0.##", CultureInfo.InvariantCulture)}%", CostLineKind.Gst,
                $"on {EstateText.Rupees(taxable)}", gst, false, true, true));
        return new CostSheetResult(rate, sqFt, lines, subtotal, gstPercent, gst, subtotal + gst);
    }

    /// <summary>How many times a charge applies to the unit: 0 (not at all), 1, or the floors above N.</summary>
    private static int Times(ChargeModel c, PropertyDocument u) => c.Applies switch
    {
        ChargeApplies.Always => 1,
        ChargeApplies.Corner => u.Corner ? 1 : 0,
        ChargeApplies.ParkFacing => u.ParkFacing ? 1 : 0,
        ChargeApplies.Floors => u.Floor is { } f && f >= (c.FloorFrom ?? int.MinValue) && f <= (c.FloorTo ?? int.MaxValue) ? 1 : 0,
        ChargeApplies.PerFloorAbove => u.Floor is { } g && g > (c.AboveFloor ?? 0) ? g - (c.AboveFloor ?? 0) : 0,
        _ => 0,
    };

    private static string BaseDetail(UnitTypeRateModel rate, AreaModel? area) => rate.Basis switch
    {
        RateBasis.PerSqFt => $"{EstateText.Rupees(rate.Rate)} per sq ft × {area!.SqFt.ToString("#,##0.##", CultureInfo.InvariantCulture)} sq ft ({AreaName(rate.Area)})",
        RateBasis.PerSqYd => $"{EstateText.Rupees(rate.Rate)} per sq yd × {(area!.SqFt / 9m).ToString("#,##0.##", CultureInfo.InvariantCulture)} sq yd ({AreaName(rate.Area)})",
        _ => "Lump sum",
    };

    private static string ChargeDetail(ChargeModel c, int times, decimal sqFt)
    {
        var each = c.Kind switch
        {
            ChargeKind.Fixed => EstateText.Rupees(c.Amount),
            ChargeKind.PerSqFt => $"{EstateText.Rupees(c.Amount)} per sq ft × {sqFt.ToString("#,##0.##", CultureInfo.InvariantCulture)} sq ft",
            ChargeKind.PerSqYd => $"{EstateText.Rupees(c.Amount)} per sq yd × {(sqFt / 9m).ToString("#,##0.##", CultureInfo.InvariantCulture)} sq yd",
            _ => $"{c.Amount.ToString("0.##", CultureInfo.InvariantCulture)}% of the basic price",
        };
        var detail = times > 1 ? $"{each} × {times} floors" : each;
        return c.Mandatory ? detail : detail + " (optional)";
    }

    private static string AreaName(AreaBasis basis) => basis switch
    {
        AreaBasis.Super => "super area",
        AreaBasis.BuiltUp => "built-up area",
        AreaBasis.Carpet => "carpet area",
        _ => "plot area",
    };

    /// <summary>
    /// The steps due before the agreement for sale: those listed before the first "on agreement" step that fall due on
    /// booking or days after it (all such steps when the plan has no agreement step).
    /// </summary>
    public static IEnumerable<int> BeforeAgreement(IReadOnlyList<PlanStepModel> steps)
    {
        var agreement = steps.ToList().FindIndex(s => s.Due == StepDue.OnAgreement);
        for (var i = 0; i < (agreement < 0 ? steps.Count : agreement); i++)
            if (steps[i].Due is StepDue.OnBooking or StepDue.DaysAfterBooking)
                yield return i;
    }

    /// <summary>RERA s.13: no more than 10% of the cost before the agreement for sale. A warning, never a refusal.</summary>
    public static IReadOnlyList<string> PlanWarnings(IReadOnlyList<PlanStepModel> steps)
    {
        var before = BeforeAgreement(steps).Select(i => steps[i]).ToList();
        var percent = before.Sum(s => s.Percent ?? 0m);
        var warnings = new List<string>();
        if (percent > 10m)
            warnings.Add($"{percent.ToString("0.##", CultureInfo.InvariantCulture)}% falls due before the agreement for sale; RERA allows at most 10% (s.13).");
        else if (before.Any(s => s.Amount is not null))
            warnings.Add("Check the fixed amounts due before the agreement for sale stay within 10% of the cost (RERA s.13).");
        return warnings;
    }

    /// <summary>
    /// A payment plan on a price (D23): fixed steps as they are, percent steps of what is left after them, in whole
    /// rupees; the last percent step takes the rounding so the steps add up to the price.
    /// </summary>
    public static PaymentScheduleDto Schedule(PaymentPlanDocument plan, decimal payable, IReadOnlyList<MilestoneModel> milestones)
    {
        var fixedSum = plan.Steps.Sum(s => s.Amount ?? 0m);
        var rest = payable - fixedSum;
        if (rest < 0)
            throw new ValidatorException($"The plan's fixed amounts ({EstateText.Rupees(fixedSum)}) are more than the price ({EstateText.Rupees(payable)}).");
        var amounts = plan.Steps.Select(s => s.Amount ?? Rupee(rest * (s.Percent ?? 0m) / 100m)).ToList();
        var lastPercent = plan.Steps.FindLastIndex(s => s.Amount is null);
        if (lastPercent >= 0)
            amounts[lastPercent] += payable - amounts.Sum();
        var steps = plan.Steps.Select((s, i) =>
        {
            var m = s.MilestoneId is { } id ? milestones.FirstOrDefault(x => x.Id == id) : null;
            return new ScheduleStepDto(s.Label, s.Percent, amounts[i], s.Due, s.Days, s.MilestoneId, m?.Name, m?.Status, m?.Date);
        }).ToList();
        var warnings = new List<string>();
        var before = BeforeAgreement(plan.Steps).Sum(i => amounts[i]);
        if (payable > 0 && before > payable / 10m)
            warnings.Add($"{EstateText.Rupees(before)} falls due before the agreement for sale, more than 10% of {EstateText.Rupees(payable)} (RERA s.13).");
        return new PaymentScheduleDto(plan.Id, plan.Name, plan.Kind, steps, amounts.Sum(), warnings);
    }
}

/// <summary>
/// Keeps a project's units in step with their type, their project and the current price list (D18, D22), and then
/// matches each unit type once (D24) — never once per unit, and only the types whose units changed in what matching reads.
/// </summary>
public sealed class ProjectUnitSync(IPropertyRepository properties, IUnitTypeRepository unitTypes, IPriceListRepository priceLists, MatchEngine engine,
    TimeProvider clock)
{
    /// <summary>
    /// The current version of the project's price list — the one units are priced from: the newest whose effective day has
    /// come (India day). A version dated later is scheduled (<see cref="PriceLists"/>).
    /// </summary>
    public async Task<PriceListDocument?> CurrentListAsync(string projectId, CancellationToken ct, string? companyId = null)
    {
        var today = EstateText.Today(clock);
        return await priceLists.GetDocuments(l => l.ProjectId == projectId && l.EffectiveFrom.CompareTo(today) <= 0, false, companyId)
            .OrderByDescending(l => l.ListVersion).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// A unit takes its type's and project's fields, and its price from the list unless it has its own. A booked or sold
    /// unit keeps the price it was taken at.
    /// </summary>
    public static void Shape(PropertyDocument unit, ProjectDocument project, UnitTypeDocument type, PriceListDocument? list)
    {
        ProjectUnits.Inherit(unit, project, type);
        if (!unit.OwnPrice && !ProjectUnits.IsTaken(unit.Status))
            unit.Price = CostSheetEngine.Compute(unit, type, list, ProjectUnits.GstFor(project, type, unit))?.Total;
        unit.PriceValue = unit.Price;
    }

    /// <summary>
    /// Every unit of the project (or of one type) brought in step; the ones that changed are saved. A type is matched again
    /// (once) only when one of its units changed in what matching reads (<see cref="MatchSignature"/>: price, status, size,
    /// place, rooms, facing …) — a photo or a note changes nothing for a customer. <paramref name="companyId"/> for a job.
    /// </summary>
    public async Task<int> RefreshAsync(ProjectDocument project, string? unitTypeId, CancellationToken ct, string? companyId = null)
    {
        var types = (await unitTypes.GetDocuments(t => t.ProjectId == project.Id, false, companyId).ToListAsync(ct)).ToDictionary(t => t.Id);
        var list = await CurrentListAsync(project.Id, ct, companyId);
        var query = properties.GetDocuments(u => u.ProjectId == project.Id, false, companyId);
        if (unitTypeId is not null)
            query = query.Where(u => u.UnitTypeId == unitTypeId);
        var changed = 0;
        var rematch = new HashSet<string>();
        foreach (var unit in await query.ToListAsync(ct))
        {
            if (unit.UnitTypeId is null || !types.TryGetValue(unit.UnitTypeId, out var type))
                continue;
            var before = unit.ToJson();
            var signature = MatchSignature(unit);
            Shape(unit, project, type, list);
            if (unit.ToJson() == before)
                continue;
            await properties.UpdateAsync(unit, ct, companyId);
            changed++;
            if (MatchSignature(unit) != signature)
                rematch.Add(type.Id);
        }
        foreach (var typeId in rematch.Order(StringComparer.Ordinal))
            await engine.ForUnitTypeAsync(project.Id, typeId, ct, companyId);
        return changed;
    }

    /// <summary>What <see cref="MatchEngine"/> reads of a unit (and shows on its matches), in one string.</summary>
    public static string MatchSignature(PropertyDocument u) => string.Join('|', new object?[]
    {
        u.Status, u.IsDeleted, u.Purpose, u.Type, u.City, u.LocalityKey, u.Locality, u.PriceValue, u.Bhk, u.SizeSqFt, u.Corner, u.ParkFacing, u.Facing,
        u.Lift, u.CoveredParking, u.OpenParking, u.Floor, u.TotalFloors, u.Furnishing, string.Join(',', u.TenantPreferences), u.UnitTypeName, u.Society,
        u.Source, u.Construction,
    }.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)));
}

internal static class ProjectLookup
{
    public static async Task<ProjectDocument> RequireAsync(IProjectRepository projects, string id, CancellationToken ct) =>
        await projects.GetDocument(id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Project not found.");

    public static async Task<UnitTypeDocument> RequireTypeAsync(IUnitTypeRepository types, string id, CancellationToken ct) =>
        await types.GetDocument(id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Unit type not found.");

    public static async Task<PropertyDocument> RequireUnitAsync(IPropertyRepository properties, string id, CancellationToken ct)
    {
        var unit = await properties.GetDocument(id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Unit not found.");
        return unit.ProjectId is null ? throw new ValidatorException($"{unit.Code} is not a project unit.") : unit;
    }
}

/// <summary>The current and scheduled versions of a price list (D22, review fixes 2026-11-11).</summary>
public static class PriceLists
{
    /// <summary>Dated later than <paramref name="today"/> (India day): stored and shown, not yet pricing the units.</summary>
    public static bool IsScheduled(PriceListDocument l, string today) => string.CompareOrdinal(l.EffectiveFrom, today) > 0;

    /// <summary>The newest version whose day has come, out of versions newest first.</summary>
    public static PriceListDocument? Current(IEnumerable<PriceListDocument> newestFirst, string today) =>
        newestFirst.FirstOrDefault(l => !IsScheduled(l, today));

    /// <summary>The next version to take effect after <paramref name="current"/>: the soonest-dated later version (the newest of a day).</summary>
    public static PriceListDocument? Next(IEnumerable<PriceListDocument> versions, PriceListDocument? current, string today) =>
        versions.Where(l => IsScheduled(l, today) && l.ListVersion > (current?.ListVersion ?? 0))
            .OrderBy(l => l.EffectiveFrom, StringComparer.Ordinal).ThenByDescending(l => l.ListVersion).FirstOrDefault();
}

/// <summary>
/// The scheduled price list step (run with the hold sweep, every ten minutes): a version saved for a later day prices the
/// units once its day comes (India day), and its unit types are matched again where the prices moved. Runs with no
/// caller, across companies, through the repositories' <c>companyId</c> overloads; one failing version is logged and the
/// rest go on.
/// </summary>
public sealed class ScheduledPriceListStep(IPriceListRepository priceLists, IProjectRepository projects, ProjectUnitSync sync, TimeProvider clock,
    Microsoft.Extensions.Logging.ILogger<ScheduledPriceListStep> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var due = await priceLists.ScheduledDueAsync(EstateText.Today(clock), 50, ct);
        var applied = 0;
        foreach (var list in due)
        {
            try
            {
                if (await projects.GetDocument(list.ProjectId, false, list.CompanyId).FirstOrDefaultAsync(ct) is { } project)
                    await sync.RefreshAsync(project, null, ct, list.CompanyId);
                await priceLists.MarkAppliedAsync(list.Id, list.CompanyId, ct);
                applied++;
                Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger,
                    "Price list v{Version} of project {ProjectId} (company {CompanyId}) took effect.", list.ListVersion, list.ProjectId, list.CompanyId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, ex,
                    "Price list {PriceListId} of company {CompanyId} could not be applied; trying again on the next run.", list.Id, list.CompanyId);
            }
        }
        return applied;
    }
}

/// <summary>Projects as members see them: counts by status, the current price list, plans; CP terms only with PBRV or PBRM (D25).</summary>
public sealed class ProjectMapper(
    IUnitTypeRepository unitTypes, IPropertyRepository properties, IPriceListRepository priceLists, IPaymentPlanRepository plans,
    IEstateSettingsRepository settings, EstateScope scope, TimeProvider clock, Enquiries.EnquiryCounts enquiries,
    Videos.EstateVideoBook videos)
{
    private sealed record UnitRow(string? ProjectId, string? UnitTypeId, PropertyStatus Status, decimal? Price);

    private async Task<List<UnitRow>> UnitRowsAsync(IReadOnlyCollection<string> projectIds, CancellationToken ct) =>
        [.. (await properties.GetDocuments(p => projectIds.Contains(p.ProjectId!))
                .Select(p => new { p.ProjectId, p.UnitTypeId, p.Status, p.PriceValue }).ToListAsync(ct))
            .Select(x => new UnitRow(x.ProjectId, x.UnitTypeId, x.Status, x.PriceValue))];

    private static decimal? FromPrice(IEnumerable<UnitRow> rows) =>
        rows.Where(r => r.Status == PropertyStatus.Available && r.Price is not null).Select(r => r.Price).Min();

    public async Task<ProjectDto> OneAsync(ProjectDocument p, CancellationToken ct)
    {
        var s = await EstateSettings.LoadAsync(settings, ct);
        var types = await unitTypes.GetDocuments(t => t.ProjectId == p.Id).OrderBy(t => t.Order).ThenBy(t => t.Name).ToListAsync(ct);
        var units = await UnitRowsAsync([p.Id], ct);
        var today = EstateText.Today(clock);
        var versions = await priceLists.GetDocuments(l => l.ProjectId == p.Id).OrderByDescending(l => l.ListVersion).Take(50).ToListAsync(ct);
        var list = PriceLists.Current(versions, today);
        var scheduled = PriceLists.Next(versions, list, today);
        var planRows = await plans.GetDocuments(x => x.ProjectId == p.Id).OrderBy(x => x.Name).ToListAsync(ct);
        var names = types.ToDictionary(t => t.Id, t => t.Name);
        var projectEnquiries = await enquiries.ForProjectsAsync([p.Id], ct);
        var typeEnquiries = await enquiries.ForUnitTypesAsync(types.Select(t => t.Id), ct);
        var projectVideos = (await videos.OfAsync(Models.Videos.EstateListingKind.Project, [p.Id], false, null, ct))[p.Id];
        // Read with PBRV or PBRM (as the deal checks read brokerage): whoever may save the terms sees them, so a save keeps them.
        var cp = (scope.Has(EstatePermissions.BrokerageView) || scope.Has(EstatePermissions.BrokerageManage)) && p.CpTerms is { } t
            ? new CpTermsDto(t.CommissionPercent, [.. t.PerUnitType.Select(x => new CpUnitTypeAmountDto(x.UnitTypeId, x.Amount))], t.PaidWhen, t.LeadValidityDays,
                t.ValidUntil, t.Notes)
            : null;

        return new ProjectDto(p.Id, p.Code, p.Name, p.Kind, p.BuilderName, p.BuilderContactName, p.BuilderPhone, p.BuilderEmail, cp, p.Categories, p.Stage,
            p.LaunchDate, p.PossessionDate,
            [.. p.Reras.Select(r => new ReraDto(r.Number, r.State, r.RegisteredOn, r.ValidUntil, r.Url))], p.ReraNotRequired, p.ReraExemptReason,
            [.. p.Approvals.Select(a => new ApprovalDto(a.Name, a.Number, a.Date))], p.OcReceivedOn,
            p.City, p.Locality, p.Address, p.Latitude, p.Longitude,
            [.. p.Landmarks.Select(l => new LandmarkDto(l.Name, l.DistanceKm, l.Minutes))], AreaMath.Dto(p.LandArea),
            [.. p.Towers.Select(x => new TowerDto(x.Name, x.Floors))], [.. p.KeyFigures.Select(k => new KeyFigureDto(k.Label, k.Value))],
            p.Overview, p.Highlights, p.Amenities, [.. p.Specifications.Select(x => new SpecificationDto(x.Section, x.Text))],
            [.. p.Photos.Select(x => new PhotoDto(x.Key, x.Url))], p.VideoUrl, p.DroneUrl, Media(p.Brochure), Photo(p.SitePlan), p.GstPercent, p.Notes,
            [.. p.Milestones.Select(m => new MilestoneDto(m.Id, m.Name, m.Status, m.Date))],
            [.. Updates(p)],
            [.. types.Select(type => UnitType(type, units.Where(u => u.UnitTypeId == type.Id).ToList()) with
            {
                Enquiries = Enquiries.EnquiryCounts.Of(typeEnquiries, type.Id),
            })],
            ProjectUnits.Count(units.Select(u => u.Status)),
            list is null ? null : PriceList(list, names, today),
            [.. planRows.Select(x => Plan(x, p.Milestones))],
            EstateSettings.IsStale(p, s, clock.GetUtcNow().UtcDateTime),
            new DateTimeOffset(DateTime.SpecifyKind(p.LastConfirmedAt, DateTimeKind.Utc)), p.CreatedBy?.ChangedDate, p.ShowOnWebsite, p.Featured, p.Slug)
        {
            Enquiries = Enquiries.EnquiryCounts.Of(projectEnquiries, p.Id),
            State = p.State,
            Videos = [.. projectVideos.Select(Videos.EstateVideoBook.Dto)],
            ScheduledPriceList = scheduled is null ? null : PriceList(scheduled, names, today),
        };
    }

    public async Task<IReadOnlyList<ProjectSummaryDto>> SummariesAsync(IReadOnlyList<ProjectDocument> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];
        var s = await EstateSettings.LoadAsync(settings, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var units = (await UnitRowsAsync([.. rows.Select(p => p.Id)], ct)).ToLookup(u => u.ProjectId);
        var counts = await enquiries.ForProjectsAsync(rows.Select(p => p.Id), ct);
        return [.. rows.Select(p => new ProjectSummaryDto(p.Id, p.Code, p.Name, p.Kind, p.Stage, p.Categories, p.City, p.Locality, p.Photos.FirstOrDefault()?.Url,
            p.PossessionDate, p.BuilderName, ProjectUnits.Count(units[p.Id].Select(u => u.Status)), FromPrice(units[p.Id]), EstateSettings.IsStale(p, s, now),
            p.ShowOnWebsite, p.Featured) { Enquiries = Enquiries.EnquiryCounts.Of(counts, p.Id) })];
    }

    public async Task<UnitTypeDto> UnitTypeAsync(UnitTypeDocument type, CancellationToken ct)
    {
        var rows = (await properties.GetDocuments(p => p.UnitTypeId == type.Id).Select(p => new { p.Status, p.PriceValue }).ToListAsync(ct))
            .Select(x => new UnitRow(type.ProjectId, type.Id, x.Status, x.PriceValue)).ToList();
        return UnitType(type, rows) with { Enquiries = Enquiries.EnquiryCounts.Of(await enquiries.ForUnitTypesAsync([type.Id], ct), type.Id) };
    }

    private static UnitTypeDto UnitType(UnitTypeDocument t, IReadOnlyList<UnitRow> units) => new(t.Id, t.ProjectId, t.Name, t.Type, t.Bhk, t.Bathrooms, t.Balconies,
        AreaMath.Dto(t.CarpetArea), AreaMath.Dto(t.BuiltUpArea), AreaMath.Dto(t.SuperArea), AreaMath.Dto(t.PlotArea), t.FrontFeet, t.DepthFeet, Photo(t.FloorPlan),
        t.PriceOnRequest, t.Notes, ProjectUnits.Count(units.Select(u => u.Status)), FromPrice(units), t.GstPercent);

    public static IEnumerable<ConstructionUpdateDto> Updates(ProjectDocument p) =>
        p.Updates.OrderByDescending(u => u.Date, StringComparer.Ordinal).Select(u => new ConstructionUpdateDto(u.Id, u.Date, u.Text, [.. u.Photos.Select(x => new PhotoDto(x.Key, x.Url))]));

    /// <param name="today">The India day, to say whether the version is still <see cref="PriceListDto.Scheduled"/>.</param>
    public static PriceListDto PriceList(PriceListDocument l, IReadOnlyDictionary<string, string> typeNames, string today) => new(l.Id, l.ProjectId, l.ListVersion,
        l.EffectiveFrom,
        [.. l.Rates.Select(r => new UnitTypeRateDto(r.UnitTypeId, typeNames.GetValueOrDefault(r.UnitTypeId), r.Basis, r.Rate, r.Area))],
        [.. l.Charges.Select(c => new ChargeDto(c.Name, c.Kind, c.Amount, c.Mandatory, c.Applies, c.FloorFrom, c.FloorTo, c.AboveFloor, c.Taxable, c.UnitTypeIds))],
        l.Notes, new DateTimeOffset(DateTime.SpecifyKind(l.CreatedAt, DateTimeKind.Utc)))
    {
        Scheduled = PriceLists.IsScheduled(l, today),
    };

    public static PaymentPlanDto Plan(PaymentPlanDocument x, IReadOnlyList<MilestoneModel> milestones) => new(x.Id, x.ProjectId, x.Name, x.Kind,
        [.. x.Steps.Select(s => new PlanStepDto(s.Label, s.Percent, s.Amount, s.Due, s.Days, s.MilestoneId,
            s.MilestoneId is { } id ? milestones.FirstOrDefault(m => m.Id == id)?.Name : null))], x.Notes, CostSheetEngine.PlanWarnings(x.Steps));

    public static MediaFileDto? Media(MediaFileModel? m) => m is null ? null : new MediaFileDto(m.Key, m.Url, m.Name);

    public static PhotoDto? Photo(PhotoModel? m) => m is null ? null : new PhotoDto(m.Key, m.Url);
}
