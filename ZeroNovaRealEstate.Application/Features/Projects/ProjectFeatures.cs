using FluentValidation;
using MongoDB.Bson;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Projects;

// ── Read ──────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>The company's projects (design 7.3), each with its units by status, by name.</summary>
public sealed record GetProjectsQuery(ProjectSearchInput Search) : IRequest<IReadOnlyList<ProjectSummaryDto>>;

public sealed class GetProjectsHandler(IProjectRepository projects, ProjectMapper mapper) : IRequestHandler<GetProjectsQuery, IReadOnlyList<ProjectSummaryDto>>
{
    public async Task<IReadOnlyList<ProjectSummaryDto>> Handle(GetProjectsQuery request, CancellationToken ct)
    {
        var f = request.Search;
        var query = projects.GetDocuments();
        if (f.Kind is { } kind)
            query = query.Where(p => p.Kind == kind);
        if (f.Stage is { } stage)
            query = query.Where(p => p.Stage == stage);
        if (f.Category is { } category)
            query = query.Where(p => p.Categories.Contains(category));
        if (EstateText.Clean(f.City) is { } city)
            query = query.Where(p => p.City == city);
        foreach (var word in (f.Text ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6))
            query = query.Where(p => p.SearchText.Contains(word));
        return await mapper.SummariesAsync(await query.OrderBy(p => p.Name).Take(300).ToListAsync(ct), ct);
    }
}

/// <summary>A project with its towers, unit types and their counts, milestones, updates, current price list and payment plans.</summary>
public sealed record GetProjectQuery(string ProjectId) : IRequest<ProjectDto>;

public sealed class GetProjectHandler(IProjectRepository projects, ProjectMapper mapper) : IRequestHandler<GetProjectQuery, ProjectDto>
{
    public async Task<ProjectDto> Handle(GetProjectQuery request, CancellationToken ct) =>
        await mapper.OneAsync(await ProjectLookup.RequireAsync(projects, request.ProjectId, ct), ct);
}

// ── Write ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Adds (no id) or changes a project (D17, design 7.2). Its units follow at once (D18): city, locality, project name,
/// amenities, construction, possession, photos, and prices (the GST %). CP terms change only for a caller with PBRM.
/// </summary>
public sealed record SaveProjectCommand(string? ProjectId, ProjectInput Input) : IRequest<ProjectDto>;

public sealed class SaveProjectValidator : AbstractValidator<SaveProjectCommand>
{
    public SaveProjectValidator()
    {
        RuleFor(x => x.Input.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Input.Locality).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Input.Overview).MaximumLength(8000);
        RuleFor(x => x.Input.Notes).MaximumLength(4000);
        RuleFor(x => x.Input.GstPercent).InclusiveBetween(0, 28).When(x => x.Input.GstPercent is not null);
        RuleFor(x => x.Input.Photos).Must(p => p is null || p.Count <= 60).WithMessage("Up to 60 photos.");
        RuleFor(x => x.Input.Towers).Must(t => t is null || t.Count <= 100).WithMessage("Up to 100 towers, blocks or phases.");
        RuleFor(x => x.Input.CpTerms!.CommissionPercent).InclusiveBetween(0, 20).When(x => x.Input.CpTerms?.CommissionPercent is not null);
    }
}

public sealed class SaveProjectHandler(
    IProjectRepository projects, IPropertyRepository properties, IUnitTypeRepository unitTypes, IEstateSettingsRepository settings,
    ILocalityRepository localities, ICounterRepository counters, ProjectUnitSync sync, ProjectMapper mapper, EstateScope scope, TimeProvider clock)
    : IRequestHandler<SaveProjectCommand, ProjectDto>
{
    public const string CodeCounter = "project";

    public async Task<ProjectDto> Handle(SaveProjectCommand request, CancellationToken ct)
    {
        var s = await EstateSettings.LoadAsync(settings, ct);
        var input = request.Input;
        var isNew = string.IsNullOrEmpty(request.ProjectId);
        var p = isNew
            ? new ProjectDocument
            {
                Id = projects.GetNewId, Code = $"PJ-{await counters.NextAsync(CodeCounter, ct):0000}", Name = string.Empty, Locality = string.Empty,
                LocalityKey = string.Empty, LastConfirmedAt = clock.GetUtcNow().UtcDateTime,
            }
            : await ProjectLookup.RequireAsync(projects, request.ProjectId!, ct);

        var partner = input.Kind == ProjectKind.Partner;
        p.Name = EstateText.Clean(input.Name)!;
        p.Kind = input.Kind;
        p.BuilderName = partner ? EstateText.Clean(input.BuilderName) ?? throw new ValidatorException("Enter the builder of a partner project.") : null;
        p.BuilderContactName = partner ? EstateText.Clean(input.BuilderContactName) : null;
        p.BuilderPhone = partner ? EstateText.Phone(input.BuilderPhone) : null;
        p.BuilderEmail = partner ? EstateText.Clean(input.BuilderEmail) : null;
        if (!partner)
            p.CpTerms = null;
        else if (scope.Has(EstatePermissions.BrokerageManage))
            p.CpTerms = await CpTermsAsync(input.CpTerms, p.Id, unitTypes, ct);

        p.Categories = [.. (input.Categories ?? []).Distinct()];
        p.Stage = input.Stage;
        p.LaunchDate = EstateText.Date(input.LaunchDate, "Launch date");
        p.PossessionDate = EstateText.Date(input.PossessionDate, "Possession date");
        p.Reras = [.. (input.Reras ?? []).Select(r => new ReraModel
        {
            Number = EstateText.Clean(r.Number) ?? throw new ValidatorException("Enter the RERA registration number."), State = EstateText.Clean(r.State),
            RegisteredOn = EstateText.Date(r.RegisteredOn, "RERA registered on"), ValidUntil = EstateText.Date(r.ValidUntil, "RERA valid until"),
            Url = EstateText.Clean(r.Url),
        })];
        p.ReraNotRequired = input.ReraNotRequired;
        p.ReraExemptReason = input.ReraNotRequired
            ? EstateText.Clean(input.ReraExemptReason) ?? throw new ValidatorException("Say why the project needs no RERA registration.")
            : null;
        p.OcReceivedOn = EstateText.Date(input.OcReceivedOn, "OC received on");
        p.Approvals = [.. (input.Approvals ?? []).Select(a => new ApprovalModel
        {
            Name = EstateText.Clean(a.Name) ?? throw new ValidatorException("Name the approval (licence, CLU, environment, fire, OC/CC …)."),
            Number = EstateText.Clean(a.Number), Date = EstateText.Date(a.Date, "Approval date"),
        })];

        p.City = EstateText.Clean(input.City) ?? s.DefaultCity;
        p.Locality = EstateText.Clean(input.Locality)!;
        p.LocalityKey = EstateText.Key(p.Locality);
        p.Address = EstateText.Clean(input.Address);
        p.Latitude = input.Latitude;
        p.Longitude = input.Longitude;
        p.Landmarks = [.. (input.Landmarks ?? []).Where(l => !string.IsNullOrWhiteSpace(l.Name)).Take(30)
            .Select(l => new LandmarkModel { Name = EstateText.Clean(l.Name)!, DistanceKm = l.DistanceKm is < 0 ? null : l.DistanceKm, Minutes = l.Minutes is < 0 ? null : l.Minutes })];

        p.LandArea = AreaMath.Model(input.LandArea, s);
        var towers = (input.Towers ?? []).Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => new TowerModel { Name = EstateText.Clean(t.Name)!, Floors = t.Floors is { } f ? (f is < 0 or > 200 ? throw new ValidatorException("Floors run 0 to 200.") : f) : null })
            .ToList();
        if (towers.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new ValidatorException($"Tower {twice.Key} is listed twice.");
        if (!isNew)
        {
            var used = await properties.GetDocuments(u => u.ProjectId == p.Id && u.Tower != null).Select(u => u.Tower!).Distinct().ToListAsync(ct);
            if (used.FirstOrDefault(name => towers.All(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))) is { } gone)
                throw new ValidatorException($"Tower {gone} has units; move or remove them first.");
        }
        p.Towers = towers;
        p.KeyFigures = [.. (input.KeyFigures ?? []).Where(k => !string.IsNullOrWhiteSpace(k.Label) && !string.IsNullOrWhiteSpace(k.Value)).Take(30)
            .Select(k => new KeyFigureModel { Label = EstateText.Clean(k.Label)!, Value = EstateText.Clean(k.Value)! })];

        p.Overview = input.Overview?.Trim() is { Length: > 0 } overview ? overview : null;
        p.Highlights = [.. (input.Highlights ?? []).Select(EstateText.Clean).OfType<string>().Take(30)];
        p.Amenities = [.. (input.Amenities ?? []).Select(EstateText.Clean).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
        p.Specifications = [.. (input.Specifications ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Section) && !string.IsNullOrWhiteSpace(x.Text)).Take(40)
            .Select(x => new SpecificationModel { Section = EstateText.Clean(x.Section)!, Text = x.Text.Trim() })];

        p.Photos = [.. (input.Photos ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Url)).Select(x => new PhotoModel { Key = x.Key.Trim(), Url = x.Url.Trim() })];
        p.VideoUrl = EstateText.Clean(input.VideoUrl);
        p.DroneUrl = EstateText.Clean(input.DroneUrl);
        p.Brochure = input.Brochure is { } b && !string.IsNullOrWhiteSpace(b.Url)
            ? new MediaFileModel { Key = b.Key.Trim(), Url = b.Url.Trim(), Name = EstateText.Clean(b.Name) }
            : null;
        p.SitePlan = input.SitePlan is { } plan && !string.IsNullOrWhiteSpace(plan.Url) ? new PhotoModel { Key = plan.Key.Trim(), Url = plan.Url.Trim() } : null;
        p.GstPercent = input.GstPercent;
        p.Notes = input.Notes?.Trim() is { Length: > 0 } notes ? notes : null;
        p.SearchText = string.Join(' ', new[] { p.Name, p.Code, p.Locality, p.City, p.BuilderName }.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();

        // The website (cms doc 06 §12): an own project is advertised only once RERA-registered, as for sharing (RERA s.3).
        if (input.ShowOnWebsite is { } show)
            p.ShowOnWebsite = show;
        if (input.Featured is { } featured)
            p.Featured = featured;
        if (p.ShowOnWebsite && !Website.WebsiteListings.MayList(p))
            throw new ValidatorException($"{p.Name} has no RERA registration yet: a project may not be advertised on the website before it is registered (RERA s.3). "
                + "Add the registration, mark the project as not needing one with the reason, or take it off the website.");
        p.Slug ??= await Website.WebsiteSlugs.ForProjectAsync(projects, p, ct);

        if (isNew)
            await projects.AddAsync(p, ct);
        else
            await projects.UpdateAsync(p, ct);
        await localities.EnsureAsync(p.Locality, p.LocalityKey, p.City, ct);
        if (!isNew)
            await sync.RefreshAsync(p, null, ct);
        return await mapper.OneAsync(p, ct);
    }

    /// <summary>CP terms; per-unit-type amounts only for this project's types.</summary>
    internal static async Task<CpTermsModel?> CpTermsAsync(CpTermsInput? input, string projectId, IUnitTypeRepository unitTypes, CancellationToken ct)
    {
        if (input is null)
            return null;
        var perType = (input.PerUnitType ?? []).GroupBy(x => x.UnitTypeId).Select(g => g.First()).ToList();
        if (perType.Count > 0)
        {
            var ids = perType.Select(x => x.UnitTypeId).ToList();
            var known = await unitTypes.GetDocuments(t => t.ProjectId == projectId && ids.Contains(t.Id)).CountAsync(ct);
            if (known != ids.Count)
                throw new ValidatorException("A CP amount names a unit type that is not this project's.");
            if (perType.Any(x => x.Amount < 0))
                throw new ValidatorException("A CP amount cannot be negative.");
        }
        if (input.LeadValidityDays is < 0 or > 3650)
            throw new ValidatorException("Lead registration validity runs 0 to 3650 days.");
        return new CpTermsModel
        {
            CommissionPercent = input.CommissionPercent,
            PerUnitType = [.. perType.Select(x => new CpUnitTypeAmountModel { UnitTypeId = x.UnitTypeId, Amount = x.Amount })],
            PaidWhen = EstateText.Clean(input.PaidWhen),
            LeadValidityDays = input.LeadValidityDays,
            ValidUntil = EstateText.Date(input.ValidUntil, "CP terms valid until"),
            Notes = EstateText.Clean(input.Notes),
        };
    }
}

/// <summary>
/// Removes a project with its unit types, units, price lists and payment plans — only while none of its units is booked
/// or sold (design 7.3). Its new matches drop.
/// </summary>
public sealed record DeleteProjectCommand(string ProjectId) : IRequest<bool>;

public sealed class DeleteProjectHandler(
    IProjectRepository projects, IPropertyRepository properties, IUnitTypeRepository unitTypes, IPriceListRepository priceLists, IPaymentPlanRepository plans,
    MatchEngine engine)
    : IRequestHandler<DeleteProjectCommand, bool>
{
    public async Task<bool> Handle(DeleteProjectCommand request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        var units = await properties.GetDocuments(u => u.ProjectId == project.Id).ToListAsync(ct);
        if (units.Count(u => ProjectUnits.IsTaken(u.Status)) is var taken and > 0)
            throw new ValidatorException($"{project.Name} has {taken} booked or sold unit{(taken == 1 ? string.Empty : "s")}; it cannot be removed.");

        var types = await unitTypes.GetDocuments(t => t.ProjectId == project.Id).ToListAsync(ct);
        await projects.DeleteAsync(project.Id, ct);
        foreach (var unit in units)
            await properties.DeleteAsync(unit.Id, ct);
        foreach (var type in types)
            await unitTypes.DeleteAsync(type.Id, ct);
        foreach (var id in await priceLists.GetDocuments(l => l.ProjectId == project.Id).Select(l => l.Id).ToListAsync(ct))
            await priceLists.DeleteAsync(id, ct);
        foreach (var id in await plans.GetDocuments(x => x.ProjectId == project.Id).Select(x => x.Id).ToListAsync(ct))
            await plans.DeleteAsync(id, ct);
        foreach (var type in types)
            await engine.ForUnitTypeAsync(project.Id, type.Id, ct);
        return true;
    }
}

/// <summary>
/// "Inventory checked with the builder" (D21): a partner project confirmed as a whole — every unit is stamped, the project
/// is fresh again and its unit types are matched again.
/// </summary>
public sealed record ConfirmProjectCommand(string ProjectId) : IRequest<ProjectDto>;

public sealed class ConfirmProjectHandler(IProjectRepository projects, IPropertyRepository properties, IUnitTypeRepository unitTypes, MatchEngine engine,
    ProjectMapper mapper, TimeProvider clock)
    : IRequestHandler<ConfirmProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(ConfirmProjectCommand request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        if (project.Kind != ProjectKind.Partner)
            throw new ValidatorException("An own project is always up to date; only a partner project is confirmed with its builder.");
        var now = clock.GetUtcNow().UtcDateTime;
        project.LastConfirmedAt = now;
        await projects.UpdateAsync(project, ct);
        await properties.StampProjectConfirmedAsync(project.Id, now, ct);
        foreach (var typeId in await unitTypes.GetDocuments(t => t.ProjectId == project.Id).Select(t => t.Id).ToListAsync(ct))
            await engine.ForUnitTypeAsync(project.Id, typeId, ct);
        return await mapper.OneAsync(project, ct);
    }
}

/// <summary>
/// The project's construction milestones (D23), as a whole list: ids kept for the ones that stay, new ones made. A
/// milestone a payment plan falls due at cannot be removed.
/// </summary>
public sealed record SaveMilestonesCommand(string ProjectId, IReadOnlyList<MilestoneInput> Milestones) : IRequest<ProjectDto>;

public sealed class SaveMilestonesHandler(IProjectRepository projects, IPaymentPlanRepository plans, ProjectMapper mapper)
    : IRequestHandler<SaveMilestonesCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(SaveMilestonesCommand request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        if (request.Milestones.Count > 60)
            throw new ValidatorException("Up to 60 milestones.");
        var known = project.Milestones.Select(m => m.Id).ToHashSet();
        var list = request.Milestones.Select(m => new MilestoneModel
        {
            Id = EstateText.Clean(m.Id) is { } id && known.Contains(id) ? id : ObjectId.GenerateNewId().ToString(),
            Name = EstateText.Clean(m.Name) ?? throw new ValidatorException("Name every milestone."),
            Status = m.Status,
            Date = EstateText.Date(m.Date, "Milestone date"),
        }).ToList();

        var removed = known.Except(list.Select(m => m.Id)).ToList();
        if (removed.Count > 0)
        {
            var using_ = await plans.GetDocuments(x => x.ProjectId == project.Id && x.Steps.Any(s => s.MilestoneId != null && removed.Contains(s.MilestoneId)))
                .FirstOrDefaultAsync(ct);
            if (using_ is not null)
                throw new ValidatorException($"The payment plan \"{using_.Name}\" falls due at a milestone being removed; change the plan first.");
        }
        project.Milestones = list;
        await projects.UpdateAsync(project, ct);
        return await mapper.OneAsync(project, ct);
    }
}

/// <summary>A construction update (date, text, photos) on the project — newest first on the project and its share page.</summary>
public sealed record AddConstructionUpdateCommand(string ProjectId, ConstructionUpdateInput Input) : IRequest<ProjectDto>;

public sealed class AddConstructionUpdateValidator : AbstractValidator<AddConstructionUpdateCommand>
{
    public AddConstructionUpdateValidator()
    {
        RuleFor(x => x.Input.Text).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Input.Photos).Must(p => p is null || p.Count <= 30).WithMessage("Up to 30 photos an update.");
    }
}

public sealed class AddConstructionUpdateHandler(IProjectRepository projects, ProjectMapper mapper) : IRequestHandler<AddConstructionUpdateCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(AddConstructionUpdateCommand request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        var input = request.Input;
        project.Updates.Add(new ConstructionUpdateModel
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Date = EstateText.Date(input.Date, "Update date") ?? throw new ValidatorException("Give the update a date."),
            Text = input.Text.Trim(),
            Photos = [.. (input.Photos ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Url)).Select(x => new PhotoModel { Key = x.Key.Trim(), Url = x.Url.Trim() })],
        });
        await projects.UpdateAsync(project, ct);
        return await mapper.OneAsync(project, ct);
    }
}

/// <summary>Removes a construction update put up by mistake.</summary>
public sealed record DeleteConstructionUpdateCommand(string ProjectId, string UpdateId) : IRequest<ProjectDto>;

public sealed class DeleteConstructionUpdateHandler(IProjectRepository projects, ProjectMapper mapper) : IRequestHandler<DeleteConstructionUpdateCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(DeleteConstructionUpdateCommand request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        if (project.Updates.RemoveAll(u => u.Id == request.UpdateId) == 0)
            throw new ValidatorException("Update not found.");
        await projects.UpdateAsync(project, ct);
        return await mapper.OneAsync(project, ct);
    }
}
