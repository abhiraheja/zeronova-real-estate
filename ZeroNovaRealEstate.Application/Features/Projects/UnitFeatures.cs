using System.Globalization;
using FluentValidation;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Projects;

// ── Unit types ────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Adds (no id) or changes a unit type; its units take the change at once (D18) and are priced again.</summary>
public sealed record SaveUnitTypeCommand(string ProjectId, string? UnitTypeId, UnitTypeInput Input) : IRequest<UnitTypeDto>;

public sealed class SaveUnitTypeValidator : AbstractValidator<SaveUnitTypeCommand>
{
    public SaveUnitTypeValidator()
    {
        RuleFor(x => x.Input.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Input.Bhk).InclusiveBetween(0, 20).When(x => x.Input.Bhk is not null);
        RuleFor(x => x.Input.Bathrooms).InclusiveBetween(0, 20).When(x => x.Input.Bathrooms is not null);
        RuleFor(x => x.Input.Balconies).InclusiveBetween(0, 20).When(x => x.Input.Balconies is not null);
        RuleFor(x => x.Input.GstPercent).InclusiveBetween(0, 28).When(x => x.Input.GstPercent is not null);
        RuleFor(x => x.Input.Notes).MaximumLength(2000);
    }
}

public sealed class SaveUnitTypeHandler(IProjectRepository projects, IUnitTypeRepository unitTypes, IEstateSettingsRepository settings, ProjectUnitSync sync,
    ProjectMapper mapper)
    : IRequestHandler<SaveUnitTypeCommand, UnitTypeDto>
{
    public async Task<UnitTypeDto> Handle(SaveUnitTypeCommand request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        var s = await EstateSettings.LoadAsync(settings, ct);
        var input = request.Input;
        var isNew = string.IsNullOrEmpty(request.UnitTypeId);
        var name = EstateText.Clean(input.Name)!;
        var type = isNew
            ? new UnitTypeDocument
            {
                Id = unitTypes.GetNewId, ProjectId = project.Id, Name = name,
                Order = await unitTypes.GetDocuments(t => t.ProjectId == project.Id).CountAsync(ct),
            }
            : await ProjectLookup.RequireTypeAsync(unitTypes, request.UnitTypeId!, ct);
        if (type.ProjectId != project.Id)
            throw new ValidatorException("This unit type is another project's.");
        var nameKey = name.ToLowerInvariant();
        if (await unitTypes.GetDocuments(t => t.ProjectId == project.Id && t.Id != type.Id).Select(t => t.Name).ToListAsync(ct) is var names
            && names.Any(n => n.ToLowerInvariant() == nameKey))
            throw new ValidatorException($"{project.Name} already has a unit type called \"{name}\".");

        var built = MatchEngine.IsBuilt(input.Type);
        type.Name = name;
        type.Type = input.Type;
        type.Bhk = built && !ProjectUnits.IsCommercial(input.Type) ? input.Bhk : null;
        type.Bathrooms = built ? input.Bathrooms : null;
        type.Balconies = built && !ProjectUnits.IsCommercial(input.Type) ? input.Balconies : null;
        type.CarpetArea = built ? AreaMath.Model(input.CarpetArea, s) : null;
        type.BuiltUpArea = built ? AreaMath.Model(input.BuiltUpArea, s) : null;
        type.SuperArea = built ? AreaMath.Model(input.SuperArea, s) : null;
        type.PlotArea = AreaMath.Model(input.PlotArea, s);
        type.FrontFeet = input.FrontFeet is < 0 ? throw new ValidatorException("Sizes cannot be negative.") : input.FrontFeet;
        type.DepthFeet = input.DepthFeet is < 0 ? throw new ValidatorException("Sizes cannot be negative.") : input.DepthFeet;
        type.FloorPlan = input.FloorPlan is { } plan && !string.IsNullOrWhiteSpace(plan.Url) ? new PhotoModel { Key = plan.Key.Trim(), Url = plan.Url.Trim() } : null;
        type.PriceOnRequest = input.PriceOnRequest;
        type.GstPercent = input.GstPercent;
        type.Notes = EstateText.Clean(input.Notes);

        if (isNew)
            await unitTypes.AddAsync(type, ct);
        else
        {
            await unitTypes.UpdateAsync(type, ct);
            await sync.RefreshAsync(project, type.Id, ct);
        }
        return await mapper.UnitTypeAsync(type, ct);
    }
}

/// <summary>Removes a unit type — only one with no units (design 7.3).</summary>
public sealed record DeleteUnitTypeCommand(string UnitTypeId) : IRequest<bool>;

public sealed class DeleteUnitTypeHandler(IUnitTypeRepository unitTypes, IPropertyRepository properties) : IRequestHandler<DeleteUnitTypeCommand, bool>
{
    public async Task<bool> Handle(DeleteUnitTypeCommand request, CancellationToken ct)
    {
        var type = await ProjectLookup.RequireTypeAsync(unitTypes, request.UnitTypeId, ct);
        if (await properties.GetDocuments(u => u.UnitTypeId == type.Id).CountAsync(ct) is var count and > 0)
            throw new ValidatorException($"{type.Name} has {count} unit{(count == 1 ? string.Empty : "s")}; remove them first.");
        await unitTypes.DeleteAsync(type.Id, ct);
        return true;
    }
}

// ── Units ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>The unit board (D20): a project's units by tower, floor and number, of one type or tower, in some statuses.</summary>
public sealed record GetProjectUnitsQuery(string ProjectId, string? UnitTypeId, string? Tower, IReadOnlyList<UnitStatus>? Statuses) : IRequest<IReadOnlyList<UnitDto>>;

public sealed class GetProjectUnitsHandler(IProjectRepository projects, IPropertyRepository properties) : IRequestHandler<GetProjectUnitsQuery, IReadOnlyList<UnitDto>>
{
    public async Task<IReadOnlyList<UnitDto>> Handle(GetProjectUnitsQuery request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        var query = properties.GetDocuments(u => u.ProjectId == project.Id);
        if (EstateText.Clean(request.UnitTypeId) is { } typeId)
            query = query.Where(u => u.UnitTypeId == typeId);
        if (EstateText.Clean(request.Tower) is { } tower)
            query = query.Where(u => u.Tower == tower);
        if (request.Statuses is { Count: > 0 } statuses)
        {
            var wanted = statuses.ToHashSet();
            var list = Enum.GetValues<PropertyStatus>().Where(s => wanted.Contains(ProjectUnits.StatusOf(s))).ToList();
            query = query.Where(u => list.Contains(u.Status));
        }
        var rows = await query.Take(5000).ToListAsync(ct);
        return [.. rows.OrderBy(u => u.Tower ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.Floor ?? int.MinValue)
            .ThenBy(u => u.UnitNumber ?? string.Empty, NaturalOrder.Instance).Select(ProjectUnits.ToDto)];
    }
}

/// <summary>
/// Makes units of a type (D19): on each floor of a tower, or a count, numbered by a pattern. A unit number is never made
/// twice in a project. The type is matched once afterwards, so fifty units are one bell, not fifty.
/// </summary>
public sealed record GenerateUnitsCommand(GenerateUnitsInput Input) : IRequest<IReadOnlyList<UnitDto>>;

public sealed class GenerateUnitsHandler(
    IProjectRepository projects, IUnitTypeRepository unitTypes, IPropertyRepository properties, ICounterRepository counters, ProjectUnitSync sync,
    MatchEngine engine, TimeProvider clock)
    : IRequestHandler<GenerateUnitsCommand, IReadOnlyList<UnitDto>>
{
    public const int Most = 1000;

    public async Task<IReadOnlyList<UnitDto>> Handle(GenerateUnitsCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var project = await ProjectLookup.RequireAsync(projects, input.ProjectId, ct);
        var type = await ProjectLookup.RequireTypeAsync(unitTypes, input.UnitTypeId, ct);
        if (type.ProjectId != project.Id)
            throw new ValidatorException("This unit type is another project's.");
        var tower = UnitRules.Tower(project, input.Tower);
        var numbers = Numbers(input, tower);

        var existing = (await properties.GetDocuments(u => u.ProjectId == project.Id && u.UnitNumber != null).Select(u => u.UnitNumber!).ToListAsync(ct))
            .Select(ProjectUnits.NumberKey).ToHashSet();
        var clash = numbers.Where(n => existing.Contains(ProjectUnits.NumberKey(n.Number))).Select(n => n.Number).Take(5).ToList();
        if (clash.Count > 0)
            throw new ValidatorException($"{project.Name} already has unit {string.Join(", ", clash)}.");

        var list = await sync.CurrentListAsync(project.Id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var made = new List<PropertyDocument>();
        foreach (var (number, floor) in numbers)
        {
            var unit = new PropertyDocument
            {
                Id = properties.GetNewId, Code = $"P-{await counters.NextAsync(Properties.SavePropertyHandler.CodeCounter, ct):0000}", Locality = project.Locality,
                LocalityKey = project.LocalityKey, LastConfirmedAt = now, ConfirmedVia = ConfirmedVia.Added, UnitNumber = number, Tower = tower, Floor = floor,
            };
            ProjectUnitSync.Shape(unit, project, type, list);
            await properties.AddAsync(unit, ct);
            made.Add(unit);
        }
        await engine.ForUnitTypeAsync(project.Id, type.Id, ct);
        return [.. made.Select(ProjectUnits.ToDto)];
    }

    /// <summary>The numbers a generate input makes, with each one's floor — refused when two would be the same.</summary>
    public static IReadOnlyList<(string Number, int? Floor)> Numbers(GenerateUnitsInput input, string? tower)
    {
        var pattern = input.Pattern?.Trim() ?? string.Empty;
        if (pattern.Length is 0 or > 60)
            throw new ValidatorException("Give a numbering pattern, such as {tower}-{floor}{nn}, SCO-{n} or Plot {n}.");
        var bare = pattern.Replace("{tower}", string.Empty).Replace("{floor}", string.Empty).Replace("{nn}", string.Empty).Replace("{n}", string.Empty);
        if (bare.Contains('{') || bare.Contains('}'))
            throw new ValidatorException("The pattern knows {tower}, {floor}, {nn} and {n} only.");
        if (!pattern.Contains("{n}") && !pattern.Contains("{nn}"))
            throw new ValidatorException("The pattern needs {n} or {nn}, so the units get different numbers.");
        if (pattern.Contains("{tower}") && tower is null)
            throw new ValidatorException("The pattern uses {tower}: pick the tower.");
        if (input.StartNumber is < 0 or > 1_000_000)
            throw new ValidatorException("Start from a number between 0 and 1,000,000.");

        var made = new List<(string, int?)>();
        var n = input.StartNumber;
        if (input.FloorFrom is { } from && input.FloorTo is { } to)
        {
            if (from > to || from < -5 || to > 200)
                throw new ValidatorException("Floors run from the lower to the higher, -5 to 200.");
            var perFloor = input.UnitsPerFloor ?? throw new ValidatorException("Say how many units each floor has.");
            if (perFloor is < 1 or > 100)
                throw new ValidatorException("A floor has 1 to 100 units.");
            if ((long)(to - from + 1) * perFloor > Most)
                throw new ValidatorException($"Up to {Most} units at a time.");
            for (var floor = from; floor <= to; floor++)
                for (var place = 1; place <= perFloor; place++)
                    made.Add((Render(pattern, tower, floor, place, n++), floor));
        }
        else
        {
            if (pattern.Contains("{floor}") || pattern.Contains("{nn}"))
                throw new ValidatorException("{floor} and {nn} need floors from and to.");
            var count = input.Count ?? throw new ValidatorException("Say how many units to make, or the floors.");
            if (count is < 1 or > Most)
                throw new ValidatorException($"Make 1 to {Most} units at a time.");
            for (var i = 0; i < count; i++)
                made.Add((Render(pattern, tower, null, i + 1, n++), null));
        }
        if (made.GroupBy(m => ProjectUnits.NumberKey(m.Item1)).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new ValidatorException($"The pattern makes {twice.First().Item1} more than once; add {{n}} or {{nn}} and {{floor}}.");
        return made;
    }

    private static string Render(string pattern, string? tower, int? floor, int place, int n) => pattern
        .Replace("{tower}", tower ?? string.Empty)
        .Replace("{floor}", floor is { } f ? f == 0 ? "G" : f < 0 ? $"B{-f}" : f.ToString(CultureInfo.InvariantCulture) : string.Empty)
        .Replace("{nn}", place.ToString("00", CultureInfo.InvariantCulture))
        .Replace("{n}", n.ToString(CultureInfo.InvariantCulture))
        .Trim();
}

/// <summary>
/// Adds one unit (no id) or changes one: its number, tower, floor and own fields, and perhaps its own price (D22). The
/// rest comes from its type and project. A unit stays in its project.
/// </summary>
public sealed record SaveUnitCommand(string? PropertyId, UnitInput Input) : IRequest<UnitDto>;

public sealed class SaveUnitValidator : AbstractValidator<SaveUnitCommand>
{
    public SaveUnitValidator()
    {
        RuleFor(x => x.Input.UnitNumber).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Input.Floor).InclusiveBetween(-5, 200).When(x => x.Input.Floor is not null);
        RuleFor(x => x.Input.OwnPrice).GreaterThan(0).LessThanOrEqualTo(100_000_000_000m).When(x => x.Input.OwnPrice is not null);
        RuleFor(x => x.Input.Notes).MaximumLength(2000);
    }
}

public sealed class SaveUnitHandler(
    IProjectRepository projects, IUnitTypeRepository unitTypes, IPropertyRepository properties, ICounterRepository counters, ProjectUnitSync sync,
    MatchEngine engine, TimeProvider clock)
    : IRequestHandler<SaveUnitCommand, UnitDto>
{
    public async Task<UnitDto> Handle(SaveUnitCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var project = await ProjectLookup.RequireAsync(projects, input.ProjectId, ct);
        var type = await ProjectLookup.RequireTypeAsync(unitTypes, input.UnitTypeId, ct);
        if (type.ProjectId != project.Id)
            throw new ValidatorException("This unit type is another project's.");
        var isNew = string.IsNullOrEmpty(request.PropertyId);
        var unit = isNew
            ? new PropertyDocument
            {
                Id = properties.GetNewId, Code = $"P-{await counters.NextAsync(Properties.SavePropertyHandler.CodeCounter, ct):0000}", Locality = project.Locality,
                LocalityKey = project.LocalityKey, LastConfirmedAt = clock.GetUtcNow().UtcDateTime, ConfirmedVia = ConfirmedVia.Added,
            }
            : await ProjectLookup.RequireUnitAsync(properties, request.PropertyId!, ct);
        if (!isNew && unit.ProjectId != project.Id)
            throw new ValidatorException("A unit stays in its project.");
        var oldType = unit.UnitTypeId;

        var number = EstateText.Clean(input.UnitNumber)!;
        var key = ProjectUnits.NumberKey(number);
        var others = await properties.GetDocuments(u => u.ProjectId == project.Id && u.Id != unit.Id && u.UnitNumber != null).Select(u => u.UnitNumber!).ToListAsync(ct);
        if (others.Any(o => ProjectUnits.NumberKey(o) == key))
            throw new ValidatorException($"{project.Name} already has unit {number}.");

        unit.UnitNumber = number;
        unit.Tower = UnitRules.Tower(project, input.Tower);
        unit.Floor = input.Floor;
        unit.Facing = input.Facing;
        unit.Corner = input.Corner;
        unit.ParkFacing = input.ParkFacing;
        unit.RoadWidthFeet = input.RoadWidthFeet is < 0 ? throw new ValidatorException("Sizes cannot be negative.") : input.RoadWidthFeet;
        unit.Notes = input.Notes?.Trim() is { Length: > 0 } notes ? notes : null;
        unit.OwnPrice = input.OwnPrice is not null;
        if (input.OwnPrice is { } own)
            unit.Price = own;
        ProjectUnitSync.Shape(unit, project, type, await sync.CurrentListAsync(project.Id, ct));

        if (isNew)
            await properties.AddAsync(unit, ct);
        else
            await properties.UpdateAsync(unit, ct);
        await engine.ForUnitTypeAsync(project.Id, type.Id, ct);
        if (oldType is not null && oldType != type.Id)
            await engine.ForUnitTypeAsync(project.Id, oldType, ct);
        return ProjectUnits.ToDto(unit);
    }
}

/// <summary>
/// A unit's status in builder words (D20): available; blocked for a lead until a date (the hold sweep releases it);
/// booked (token received); sold; withdrawn. Its unit type's matches follow at once (D24).
/// </summary>
public sealed record SetUnitStatusCommand(string PropertyId, UnitStatusInput Input) : IRequest<UnitDto>;

public sealed class SetUnitStatusHandler(IPropertyRepository properties, MatchEngine engine) : IRequestHandler<SetUnitStatusCommand, UnitDto>
{
    public async Task<UnitDto> Handle(SetUnitStatusCommand request, CancellationToken ct)
    {
        var unit = await ProjectLookup.RequireUnitAsync(properties, request.PropertyId, ct);
        var input = request.Input;
        unit.Status = ProjectUnits.PropertyStatusOf(input.Status);
        if (input.Status == UnitStatus.Blocked)
        {
            unit.HoldUntil = EstateText.Date(input.HoldUntil, "Blocked until") ?? throw new ValidatorException("Say until when the unit is blocked.");
            unit.HoldLeadId = EstateText.Clean(input.LeadId) ?? throw new ValidatorException("Say which customer (lead) the unit is blocked for.");
            unit.HoldLeadName = EstateText.Clean(input.LeadName);
        }
        else
        {
            unit.HoldUntil = null;
            unit.HoldLeadId = input.Status == UnitStatus.Booked ? EstateText.Clean(input.LeadId) ?? unit.HoldLeadId : null;
            unit.HoldLeadName = input.Status == UnitStatus.Booked ? EstateText.Clean(input.LeadName) ?? unit.HoldLeadName : null;
        }
        await properties.UpdateAsync(unit, ct);
        await engine.ForPropertyAsync(unit, ct);
        return ProjectUnits.ToDto(unit);
    }
}

internal static class UnitRules
{
    /// <summary>The tower as the project names it; a project with towers takes only those.</summary>
    public static string? Tower(ProjectDocument project, string? tower)
    {
        if (EstateText.Clean(tower) is not { } name)
            return null;
        if (project.Towers.Count == 0)
            return name;
        return project.Towers.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))?.Name
            ?? throw new ValidatorException($"{project.Name} has no tower {name}. Add it to the project first.");
    }
}

/// <summary>"A-2" before "A-10": digits compared as numbers.</summary>
internal sealed class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        x ??= string.Empty;
        y ??= string.Empty;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                var byLength = a.Length.CompareTo(b.Length);
                if (byLength != 0)
                    return byLength;
                var byDigits = string.CompareOrdinal(a, b);
                if (byDigits != 0)
                    return byDigits;
                continue;
            }
            var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (c != 0)
                return c;
            i++;
            j++;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
