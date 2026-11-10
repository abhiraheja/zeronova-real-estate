using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Models.Projects;

/// <summary>Whose project it is (D17): the company is the builder, or it sells another builder's project as a channel partner.</summary>
public enum ProjectKind
{
    Own,
    Partner,
}

public enum ProjectCategory
{
    Residential,
    Commercial,
    MixedUse,
    Plotted,
    Farmhouse,
    Industrial,
    GroupHousing,
}

public enum ProjectStage
{
    Upcoming,
    NewLaunch,
    UnderConstruction,
    ReadyToMove,
    Completed,
}

public enum MilestoneStatus
{
    Pending,
    InProgress,
    Done,
}

/// <summary>
/// A unit's status in builder words (D20), kept on the unit as a property status: Blocked = on hold (for a lead, until a
/// date), Booked = token received.
/// </summary>
public enum UnitStatus
{
    Available,
    Blocked,
    Booked,
    Sold,
    Withdrawn,
}

/// <summary>How a unit type's base price is reckoned (D22).</summary>
public enum RateBasis
{
    PerSqFt,
    PerSqYd,
    LumpSum,
}

/// <summary>Which of the unit type's areas a per-area rate or charge is reckoned on.</summary>
public enum AreaBasis
{
    Super,
    BuiltUp,
    Carpet,
    Plot,
}

/// <summary>A charge on the price list: a fixed amount, per sq ft or sq yd of the rate's area, or a percent of the base price.</summary>
public enum ChargeKind
{
    Fixed,
    PerSqFt,
    PerSqYd,
    PercentOfBase,
}

/// <summary>When a charge applies to a unit: always, corner or park-facing units, floors from–to, or per floor above N.</summary>
public enum ChargeApplies
{
    Always,
    Corner,
    ParkFacing,
    Floors,
    PerFloorAbove,
}

public enum PaymentPlanKind
{
    ConstructionLinked,
    DownPayment,
    PossessionLinked,
    Flexi,
    Other,
}

/// <summary>
/// When a payment-plan step falls due (D23). <see cref="OnAgreement"/> is the agreement for sale: RERA s.13 caps what is
/// taken before it at 10% of the cost.
/// </summary>
public enum StepDue
{
    OnBooking,
    DaysAfterBooking,
    AtMilestone,
    OnPossession,
    OnAgreement,
}

public enum CostLineKind
{
    Base,
    Charge,
    Gst,
}

// ── Inputs ────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>A file already uploaded to zeronova-document-helper's public bucket (a brochure PDF): key, address and its name.</summary>
public sealed record MediaFileInput(string Key, string Url, string? Name);

/// <summary>
/// A partner project's channel-partner terms (D17): commission as a percent of the agreement value or an amount per unit
/// type, when it is paid, how long a registered lead stays the CP's, until when the terms hold.
/// </summary>
public sealed record CpTermsInput(
    decimal? CommissionPercent,
    IReadOnlyList<CpUnitTypeAmountInput>? PerUnitType,
    string? PaidWhen,
    int? LeadValidityDays,
    string? ValidUntil,
    string? Notes);

public sealed record CpUnitTypeAmountInput(string UnitTypeId, decimal Amount);

/// <summary>A RERA registration (several for phases). Dates yyyy-MM-dd.</summary>
public sealed record ReraInput(string Number, string? State, string? RegisteredOn, string? ValidUntil, string? Url);

/// <summary>An approval: licence, CLU, environment, fire, OC/CC … Date yyyy-MM-dd.</summary>
public sealed record ApprovalInput(string Name, string? Number, string? Date);

/// <summary>A landmark near the project, with its distance in km or minutes.</summary>
public sealed record LandmarkInput(string Name, decimal? DistanceKm, int? Minutes);

/// <summary>A tower, block or phase and its floors.</summary>
public sealed record TowerInput(string Name, int? Floors);

public sealed record KeyFigureInput(string Label, string Value);

public sealed record SpecificationInput(string Section, string Text);

/// <summary>
/// A project as entered (design 7.2). Only the name, kind and locality are required. The builder and CP terms are a
/// partner project's; <see cref="CpTerms"/> is applied only for a caller with PBRM (kept as it is otherwise).
/// </summary>
public sealed class ProjectInput
{
    public string Name { get; init; } = string.Empty;
    public ProjectKind Kind { get; init; }

    // ── Partner
    public string? BuilderName { get; init; }
    public string? BuilderContactName { get; init; }
    public string? BuilderPhone { get; init; }
    public string? BuilderEmail { get; init; }
    public CpTermsInput? CpTerms { get; init; }

    // ── Kind and stage
    public IReadOnlyList<ProjectCategory>? Categories { get; init; }
    public ProjectStage Stage { get; init; } = ProjectStage.UnderConstruction;

    /// <summary>yyyy-MM-dd.</summary>
    public string? LaunchDate { get; init; }

    /// <summary>yyyy-MM-dd.</summary>
    public string? PossessionDate { get; init; }

    // ── RERA and approvals
    public IReadOnlyList<ReraInput>? Reras { get; init; }

    /// <summary>
    /// The project needs no RERA registration (small plots, exempt categories) — with <see cref="ReraExemptReason"/>. An
    /// own project without a registration and without this cannot be shared (RERA s.3: no advertising before it).
    /// </summary>
    public bool ReraNotRequired { get; init; }

    public string? ReraExemptReason { get; init; }
    public IReadOnlyList<ApprovalInput>? Approvals { get; init; }

    /// <summary>yyyy-MM-dd the occupancy certificate came. From then unsold units carry no GST on their cost sheet.</summary>
    public string? OcReceivedOn { get; init; }

    // ── Location
    public string? City { get; init; }
    public string Locality { get; init; } = string.Empty;
    public string? Address { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public IReadOnlyList<LandmarkInput>? Landmarks { get; init; }

    // ── Size and figures
    public AreaInput? LandArea { get; init; }
    public IReadOnlyList<TowerInput>? Towers { get; init; }
    public IReadOnlyList<KeyFigureInput>? KeyFigures { get; init; }

    // ── Description
    public string? Overview { get; init; }
    public IReadOnlyList<string>? Highlights { get; init; }
    public IReadOnlyList<string>? Amenities { get; init; }
    public IReadOnlyList<SpecificationInput>? Specifications { get; init; }

    // ── Media (uploaded to zeronova-document-helper's public bucket, like property photos)
    public IReadOnlyList<PhotoInput>? Photos { get; init; }
    public string? VideoUrl { get; init; }
    public string? DroneUrl { get; init; }
    public MediaFileInput? Brochure { get; init; }
    public PhotoInput? SitePlan { get; init; }

    // ── Sales
    /// <summary>
    /// The project's GST % on taxable lines of the cost sheet — typed in, never assumed; a unit type's own GST % wins
    /// over it. Null on both = no GST line.
    /// </summary>
    public decimal? GstPercent { get; init; }

    public string? Notes { get; init; }

    // ── The company's website (cms doc 06 §12). Null keeps what is saved. An own project is listed only with its RERA
    // registration, or marked as not needing one (RERA s.3), as for sharing.

    public bool? ShowOnWebsite { get; init; }
    public bool? Featured { get; init; }
}

public sealed class ProjectSearchInput
{
    public string? Text { get; init; }
    public ProjectKind? Kind { get; init; }
    public ProjectStage? Stage { get; init; }
    public ProjectCategory? Category { get; init; }
    public string? City { get; init; }
}

/// <summary>A construction milestone; no id = a new one. Date yyyy-MM-dd (planned, or when it was reached).</summary>
public sealed record MilestoneInput(string? Id, string Name, MilestoneStatus Status, string? Date);

/// <summary>A construction update: when, what, and photos (public bucket).</summary>
public sealed record ConstructionUpdateInput(string Date, string Text, IReadOnlyList<PhotoInput>? Photos);

/// <summary>
/// A unit type (design 7.2): what its units are. Units take its type, BHK, bathrooms, balconies, areas and dimensions,
/// and keep them in step when it changes (D18).
/// </summary>
public sealed class UnitTypeInput
{
    public string Name { get; init; } = string.Empty;
    public PropertyType Type { get; init; }
    public int? Bhk { get; init; }
    public int? Bathrooms { get; init; }
    public int? Balconies { get; init; }
    public AreaInput? CarpetArea { get; init; }
    public AreaInput? BuiltUpArea { get; init; }
    public AreaInput? SuperArea { get; init; }
    public AreaInput? PlotArea { get; init; }
    public decimal? FrontFeet { get; init; }
    public decimal? DepthFeet { get; init; }
    public PhotoInput? FloorPlan { get; init; }

    /// <summary>Hides this type's prices on share pages (D22).</summary>
    public bool PriceOnRequest { get; init; }

    /// <summary>
    /// This type's GST % (residential 5%, affordable 1%, commercial …), over the project's. Null = the project's.
    /// </summary>
    public decimal? GstPercent { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// Makes units of a type (D19). With floors: <see cref="UnitsPerFloor"/> on every floor from <see cref="FloorFrom"/> to
/// <see cref="FloorTo"/> (0 = ground). Without: <see cref="Count"/> units. The pattern takes <c>{tower}</c>,
/// <c>{floor}</c> (G for ground, B1 for the first basement), <c>{nn}</c> (the place on its floor, two digits) and
/// <c>{n}</c> (a running number from <see cref="StartNumber"/>): <c>{tower}-{floor}{nn}</c>, <c>SCO-{n}</c>, <c>Plot {n}</c>.
/// </summary>
public sealed class GenerateUnitsInput
{
    public string ProjectId { get; init; } = string.Empty;
    public string UnitTypeId { get; init; } = string.Empty;
    public string? Tower { get; init; }
    public int? FloorFrom { get; init; }
    public int? FloorTo { get; init; }
    public int? UnitsPerFloor { get; init; }
    public int? Count { get; init; }
    public string Pattern { get; init; } = string.Empty;
    public int StartNumber { get; init; } = 1;
}

/// <summary>
/// One unit, added or changed: its number, tower, floor and its own fields; the rest comes from its type and project.
/// <see cref="OwnPrice"/> set = the unit's own price instead of the price list's cost sheet.
/// </summary>
public sealed class UnitInput
{
    public string ProjectId { get; init; } = string.Empty;
    public string UnitTypeId { get; init; } = string.Empty;
    public string UnitNumber { get; init; } = string.Empty;
    public string? Tower { get; init; }

    /// <summary>0 = ground, -1 = basement.</summary>
    public int? Floor { get; init; }

    public Facing? Facing { get; init; }
    public bool Corner { get; init; }
    public bool ParkFacing { get; init; }
    public decimal? RoadWidthFeet { get; init; }
    public decimal? OwnPrice { get; init; }
    public string? Notes { get; init; }
}

/// <summary>A unit's new status (D20). Blocked needs the lead and the date it is held until (yyyy-MM-dd).</summary>
public sealed record UnitStatusInput(UnitStatus Status, string? HoldUntil, string? LeadId, string? LeadName);

/// <summary>A unit type's base rate on the price list (D22): per sq ft, per sq yd or a lump sum, on a chosen area.</summary>
public sealed record UnitTypeRateInput(string UnitTypeId, RateBasis Basis, decimal Rate, AreaBasis Area = AreaBasis.Super);

/// <summary>
/// A charge (PLC, floor rise, EDC/IDC, parking, club …). <see cref="Amount"/> is rupees (fixed), rupees per sq ft or sq
/// yd of the rate's area, or a percent of the base price. Per floor above N multiplies it by the floors above N. An
/// optional charge is shown on the cost sheet but not added to the total. Empty <see cref="UnitTypeIds"/> = every type.
/// </summary>
public sealed record ChargeInput(
    string Name,
    ChargeKind Kind,
    decimal Amount,
    bool Mandatory = true,
    ChargeApplies Applies = ChargeApplies.Always,
    int? FloorFrom = null,
    int? FloorTo = null,
    int? AboveFloor = null,
    bool Taxable = true,
    IReadOnlyList<string>? UnitTypeIds = null);

/// <summary>A new version of the project's price list; the old ones are kept. Effective from yyyy-MM-dd (default today).</summary>
public sealed record PriceListInput(IReadOnlyList<UnitTypeRateInput> Rates, IReadOnlyList<ChargeInput>? Charges, string? EffectiveFrom, string? Notes);

/// <summary>
/// A payment-plan step (D23): a percent or a fixed amount, and when it falls due. Percents are of what is left after the
/// plan's fixed amounts, and must total 100%.
/// </summary>
public sealed record PlanStepInput(string Label, decimal? Percent, decimal? Amount, StepDue Due, int? Days, string? MilestoneId);

public sealed record PaymentPlanInput(string Name, PaymentPlanKind Kind, IReadOnlyList<PlanStepInput> Steps, string? Notes);

// ── DTOs ──────────────────────────────────────────────────────────────────────────────────────────────

public sealed record MediaFileDto(string Key, string Url, string? Name);

public sealed record CpUnitTypeAmountDto(string UnitTypeId, decimal Amount);

/// <summary>A partner project's CP terms — null for a caller without PBRV.</summary>
public sealed record CpTermsDto(
    decimal? CommissionPercent,
    IReadOnlyList<CpUnitTypeAmountDto> PerUnitType,
    string? PaidWhen,
    int? LeadValidityDays,
    string? ValidUntil,
    string? Notes);

public sealed record ReraDto(string Number, string? State, string? RegisteredOn, string? ValidUntil, string? Url);

public sealed record ApprovalDto(string Name, string? Number, string? Date);

public sealed record LandmarkDto(string Name, decimal? DistanceKm, int? Minutes);

public sealed record TowerDto(string Name, int? Floors);

public sealed record KeyFigureDto(string Label, string Value);

public sealed record SpecificationDto(string Section, string Text);

public sealed record MilestoneDto(string Id, string Name, MilestoneStatus Status, string? Date);

public sealed record ConstructionUpdateDto(string Id, string Date, string Text, IReadOnlyList<PhotoDto> Photos);

/// <summary>Units by status (D20) — of a project, a unit type or a tower.</summary>
public sealed record UnitCountsDto(int Total, int Available, int Blocked, int Booked, int Sold, int Withdrawn);

/// <summary>A unit type with its units' counts and the lowest price of its available units (the "from" price).</summary>
public sealed record UnitTypeDto(
    string Id,
    string ProjectId,
    string Name,
    PropertyType Type,
    int? Bhk,
    int? Bathrooms,
    int? Balconies,
    AreaDto? CarpetArea,
    AreaDto? BuiltUpArea,
    AreaDto? SuperArea,
    AreaDto? PlotArea,
    decimal? FrontFeet,
    decimal? DepthFeet,
    PhotoDto? FloorPlan,
    bool PriceOnRequest,
    string? Notes,
    UnitCountsDto Units,
    decimal? FromPrice,
    decimal? GstPercent)
{
    /// <summary>Enquiries on this unit type and its units (D30).</summary>
    public ZeroNovaRealEstate.Models.Enquiries.EnquiryCountsDto Enquiries { get; init; } = ZeroNovaRealEstate.Models.Enquiries.EnquiryCountsDto.None;
}

public sealed record UnitTypeRateDto(string UnitTypeId, string? UnitTypeName, RateBasis Basis, decimal Rate, AreaBasis Area);

public sealed record ChargeDto(
    string Name,
    ChargeKind Kind,
    decimal Amount,
    bool Mandatory,
    ChargeApplies Applies,
    int? FloorFrom,
    int? FloorTo,
    int? AboveFloor,
    bool Taxable,
    IReadOnlyList<string> UnitTypeIds);

/// <summary>One version of a project's price list (D22). The newest version is the one units are priced from.</summary>
public sealed record PriceListDto(
    string Id,
    string ProjectId,
    int Version,
    string EffectiveFrom,
    IReadOnlyList<UnitTypeRateDto> Rates,
    IReadOnlyList<ChargeDto> Charges,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record PlanStepDto(string Label, decimal? Percent, decimal? Amount, StepDue Due, int? Days, string? MilestoneId, string? MilestoneName);

/// <summary>
/// A payment plan. <see cref="Warnings"/> are not errors: e.g. more than 10% due before the agreement for sale (RERA s.13).
/// </summary>
public sealed record PaymentPlanDto(string Id, string ProjectId, string Name, PaymentPlanKind Kind, IReadOnlyList<PlanStepDto> Steps, string? Notes,
    IReadOnlyList<string> Warnings);

/// <summary>
/// A project as members see it (design 7.3): its facts and media, towers, unit types with their counts, milestones,
/// construction updates, the current price list and the payment plans. <see cref="CpTerms"/> is null without PBRV.
/// A partner project is <see cref="Stale"/> past the dealer's sale freshness days since it was last confirmed (D21).
/// </summary>
public sealed record ProjectDto(
    string Id,
    string Code,
    string Name,
    ProjectKind Kind,
    string? BuilderName,
    string? BuilderContactName,
    string? BuilderPhone,
    string? BuilderEmail,
    CpTermsDto? CpTerms,
    IReadOnlyList<ProjectCategory> Categories,
    ProjectStage Stage,
    string? LaunchDate,
    string? PossessionDate,
    IReadOnlyList<ReraDto> Reras,
    bool ReraNotRequired,
    string? ReraExemptReason,
    IReadOnlyList<ApprovalDto> Approvals,
    string? OcReceivedOn,
    string? City,
    string Locality,
    string? Address,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<LandmarkDto> Landmarks,
    AreaDto? LandArea,
    IReadOnlyList<TowerDto> Towers,
    IReadOnlyList<KeyFigureDto> KeyFigures,
    string? Overview,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<SpecificationDto> Specifications,
    IReadOnlyList<PhotoDto> Photos,
    string? VideoUrl,
    string? DroneUrl,
    MediaFileDto? Brochure,
    PhotoDto? SitePlan,
    decimal? GstPercent,
    string? Notes,
    IReadOnlyList<MilestoneDto> Milestones,
    IReadOnlyList<ConstructionUpdateDto> Updates,
    IReadOnlyList<UnitTypeDto> UnitTypes,
    UnitCountsDto Units,
    PriceListDto? PriceList,
    IReadOnlyList<PaymentPlanDto> PaymentPlans,
    bool Stale,
    DateTimeOffset LastConfirmedAt,
    DateTimeOffset? CreatedAt,
    bool ShowOnWebsite = false,
    bool Featured = false,
    string? WebsiteSlug = null)
{
    /// <summary>Enquiries on this project, its unit types and its units (D30).</summary>
    public ZeroNovaRealEstate.Models.Enquiries.EnquiryCountsDto Enquiries { get; init; } = ZeroNovaRealEstate.Models.Enquiries.EnquiryCountsDto.None;
}

/// <summary>A project in a list (and on the dashboard): what it is and its units by status.</summary>
public sealed record ProjectSummaryDto(
    string Id,
    string Code,
    string Name,
    ProjectKind Kind,
    ProjectStage Stage,
    IReadOnlyList<ProjectCategory> Categories,
    string? City,
    string Locality,
    string? CoverUrl,
    string? PossessionDate,
    string? BuilderName,
    UnitCountsDto Units,
    decimal? FromPrice,
    bool Stale,
    bool ShowOnWebsite = false,
    bool Featured = false)
{
    /// <summary>Enquiries on this project, its unit types and its units (D30) — as <see cref="ProjectDto.Enquiries"/>.</summary>
    public ZeroNovaRealEstate.Models.Enquiries.EnquiryCountsDto Enquiries { get; init; } = ZeroNovaRealEstate.Models.Enquiries.EnquiryCountsDto.None;
}

/// <summary>A unit on the board (D20): its number, tower, floor, type, status (and the lead it is blocked for), price.</summary>
public sealed record UnitDto(
    string Id,
    string Code,
    string ProjectId,
    string UnitTypeId,
    string? UnitTypeName,
    string? Tower,
    int? Floor,
    string? UnitNumber,
    UnitStatus Status,
    PropertyStatus PropertyStatus,
    string? HoldUntil,
    string? HoldLeadId,
    string? HoldLeadName,
    decimal? Price,
    bool OwnPrice,
    decimal? SizeSqFt,
    Facing? Facing,
    bool Corner,
    bool ParkFacing,
    decimal? RoadWidthFeet,
    string? Notes);

/// <summary>A cost-sheet line: the base price, a charge (optional ones are shown, not added), or GST.</summary>
public sealed record CostLineDto(string Name, CostLineKind Kind, string? Detail, decimal Amount, bool Taxable, bool Mandatory, bool Included);

public sealed record ScheduleStepDto(
    string Label,
    decimal? Percent,
    decimal Amount,
    StepDue Due,
    int? Days,
    string? MilestoneId,
    string? MilestoneName,
    MilestoneStatus? MilestoneStatus,
    string? MilestoneDate);

/// <summary>
/// A payment plan applied to a unit's price: each step's amount (to the rupee; the last percent step takes the rounding),
/// and warnings such as more than 10% before the agreement for sale (RERA s.13).
/// </summary>
public sealed record PaymentScheduleDto(string PlanId, string Name, PaymentPlanKind Kind, IReadOnlyList<ScheduleStepDto> Steps, decimal Total,
    IReadOnlyList<string> Warnings);

/// <summary>
/// A unit's cost sheet (D22) from the project's current price list: the base price on the chosen area, its charges, GST
/// on the taxable lines (the type's GST %, else the project's; none for an unsold unit once the OC is received), the
/// total. The carpet area and the basic and total price per sq ft of carpet area are always given (RERA prices on carpet
/// area), whatever area the rate is on. <see cref="Payable"/> is the unit's price — its own price when it has one, else
/// the total. With a plan, the schedule of that price. Lines are empty when the price list has no rate for the type.
/// </summary>
public sealed record CostSheetDto(
    string PropertyId,
    string Code,
    string? UnitNumber,
    string? UnitTypeName,
    string? PriceListId,
    int? PriceListVersion,
    RateBasis? RateBasis,
    decimal? Rate,
    AreaBasis? AreaBasis,
    decimal? AreaSqFt,
    decimal? CarpetAreaSqFt,
    decimal? BasicPerCarpetSqFt,
    decimal? TotalPerCarpetSqFt,
    IReadOnlyList<CostLineDto> Lines,
    decimal Subtotal,
    decimal? GstPercent,
    decimal Gst,
    decimal Total,
    decimal? OwnPrice,
    decimal? Payable,
    PaymentScheduleDto? Schedule);

/// <summary>A link to send a project (its page: facts, media, brochure, unit types; never CP terms or unit numbers).</summary>
public sealed record ProjectShareDto(string Id, string Token, string Url, string ProjectId, string? RequirementId, int Opens, DateTimeOffset? LastOpenedAt,
    DateTimeOffset CreatedAt, string Message, int Enquiries = 0);

/// <summary>A unit type on the shared page: its facts, how many are available, and its "from" price (null when on request).</summary>
public sealed record SharedUnitTypeDto(
    string Name,
    PropertyType Type,
    int? Bhk,
    int? Bathrooms,
    int? Balconies,
    AreaDto? CarpetArea,
    AreaDto? BuiltUpArea,
    AreaDto? SuperArea,
    AreaDto? PlotArea,
    PhotoDto? FloorPlan,
    decimal? FromPrice,
    bool PriceOnRequest,
    int Available);

/// <summary>What a customer sees on a shared project: never CP terms, unit numbers, the builder's contact or owners.</summary>
public sealed record SharedProjectDto(
    string Name,
    IReadOnlyList<ProjectCategory> Categories,
    ProjectStage Stage,
    string? City,
    string Locality,
    string? Address,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<LandmarkDto> Landmarks,
    AreaDto? LandArea,
    IReadOnlyList<TowerDto> Towers,
    IReadOnlyList<KeyFigureDto> KeyFigures,
    string? Overview,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<SpecificationDto> Specifications,
    IReadOnlyList<PhotoDto> Photos,
    string? VideoUrl,
    string? DroneUrl,
    MediaFileDto? Brochure,
    PhotoDto? SitePlan,
    IReadOnlyList<ReraDto> Reras,
    string? LaunchDate,
    string? PossessionDate,
    IReadOnlyList<ConstructionUpdateDto> Updates,
    IReadOnlyList<SharedUnitTypeDto> UnitTypes,
    string? DealerName,
    string? DealerPhone,
    string? ReraNumber,
    bool Active);
