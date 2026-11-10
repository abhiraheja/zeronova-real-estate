using System.Security.Cryptography;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Configuration;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Application.Features.Projects;

/// <summary>
/// A link to send a project (design 7.3): its facts, media, brochure and unit types with "from" prices — never CP terms,
/// unit numbers or the builder's contact — that counts its opens. For a customer's requirement it marks their new
/// matches to the project shown. An own project needs its RERA registration first (RERA s.3: no advertising before it),
/// unless it is marked as not needing one.
/// </summary>
public sealed record CreateProjectShareCommand(string ProjectId, string? RequirementId) : IRequest<ProjectShareDto>;

public sealed class CreateProjectShareHandler(IShareRepository shares, IProjectRepository projects, IRequirementRepository requirements, IMatchRepository matches,
    IPropertyRepository properties, IUnitTypeRepository unitTypes, ShareOptions options, EstateScope scope, TimeProvider clock)
    : IRequestHandler<CreateProjectShareCommand, ProjectShareDto>
{
    public async Task<ProjectShareDto> Handle(CreateProjectShareCommand request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        if (project.Kind == ProjectKind.Own && project.Reras.Count == 0 && !project.ReraNotRequired)
            throw new ValidatorException($"{project.Name} has no RERA registration yet: a project may not be advertised before it is registered (RERA s.3). "
                + "Add the registration, or mark the project as not needing one with the reason.");
        RequirementDocument? requirement = null;
        if (EstateText.Clean(request.RequirementId) is { } requirementId)
        {
            requirement = await requirements.GetDocument(requirementId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Requirement not found.");
            scope.RequireWork(requirement);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var share = new ShareDocument
        {
            Id = shares.GetNewId, Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_').TrimEnd('='),
            ProjectId = project.Id, RequirementId = requirement?.Id, CreatedAt = now,
        };
        await shares.AddAsync(share, ct);

        if (requirement is not null)
            foreach (var match in await matches.GetDocuments(m => m.ProjectId == project.Id && m.RequirementId == requirement.Id && m.Status == MatchStatus.New)
                         .ToListAsync(ct))
            {
                match.Status = MatchStatus.Shown;
                match.UpdatedAt = now;
                await matches.UpdateAsync(match, ct);
            }
        var from = await ProjectShareMapping.FromPriceAsync(project, properties, unitTypes, ct);
        return ProjectShareMapping.ToDto(share, project, options, requirement?.LeadName, from);
    }
}

public sealed record GetProjectSharesQuery(string ProjectId) : IRequest<IReadOnlyList<ProjectShareDto>>;

public sealed class GetProjectSharesHandler(IShareRepository shares, IProjectRepository projects, IRequirementRepository requirements, IPropertyRepository properties,
    IUnitTypeRepository unitTypes, ShareOptions options)
    : IRequestHandler<GetProjectSharesQuery, IReadOnlyList<ProjectShareDto>>
{
    public async Task<IReadOnlyList<ProjectShareDto>> Handle(GetProjectSharesQuery request, CancellationToken ct)
    {
        var project = await ProjectLookup.RequireAsync(projects, request.ProjectId, ct);
        var rows = await shares.GetDocuments(s => s.ProjectId == project.Id).OrderByDescending(s => s.CreatedAt).Take(100).ToListAsync(ct);
        var reqIds = rows.Select(r => r.RequirementId).OfType<string>().Distinct().ToList();
        var names = (await requirements.GetDocuments(r => reqIds.Contains(r.Id), true).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.LeadName);
        var from = await ProjectShareMapping.FromPriceAsync(project, properties, unitTypes, ct);
        return [.. rows.Select(r => ProjectShareMapping.ToDto(r, project, options, r.RequirementId is { } id ? names.GetValueOrDefault(id) : null, from))];
    }
}

/// <summary>The shared project page, for anyone with the link. Each open is counted.</summary>
public sealed record GetSharedProjectQuery(string Token) : IRequest<SharedProjectDto>;

public sealed class GetSharedProjectHandler(IShareRepository shares, IProjectRepository projects, IUnitTypeRepository unitTypes, IPropertyRepository properties,
    IEstateSettingsRepository settings, TimeProvider clock)
    : IRequestHandler<GetSharedProjectQuery, SharedProjectDto>
{
    public async Task<SharedProjectDto> Handle(GetSharedProjectQuery request, CancellationToken ct)
    {
        var share = await shares.FindByTokenAnyTenantAsync(request.Token.Trim(), ct) ?? throw new ValidatorException("This link is not valid.");
        if (share.ProjectId is not { } projectId)
            throw new ValidatorException("This link is not valid.");
        var company = share.CompanyId;
        var p = await projects.GetDocument(projectId, true, company).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("This link is not valid.");
        var s = await EstateSettings.LoadAsync(settings, ct, company);
        var types = await unitTypes.GetDocuments(t => t.ProjectId == p.Id, false, company).OrderBy(t => t.Order).ThenBy(t => t.Name).ToListAsync(ct);
        var units = (await properties.GetDocuments(u => u.ProjectId == p.Id, false, company).Select(u => new { u.UnitTypeId, u.Status, u.PriceValue }).ToListAsync(ct))
            .ToLookup(u => u.UnitTypeId);
        await shares.RecordOpenAsync(share.Id, company, clock.GetUtcNow().UtcDateTime, ct);

        return new SharedProjectDto(p.Name, p.Categories, p.Stage, p.City, p.Locality, p.Address, p.Latitude, p.Longitude,
            [.. p.Landmarks.Select(l => new LandmarkDto(l.Name, l.DistanceKm, l.Minutes))], AreaMath.Dto(p.LandArea),
            [.. p.Towers.Select(t => new TowerDto(t.Name, t.Floors))], [.. p.KeyFigures.Select(k => new KeyFigureDto(k.Label, k.Value))],
            p.Overview, p.Highlights, p.Amenities, [.. p.Specifications.Select(x => new SpecificationDto(x.Section, x.Text))],
            [.. p.Photos.Select(x => new PhotoDto(x.Key, x.Url))], p.VideoUrl, p.DroneUrl, ProjectMapper.Media(p.Brochure), ProjectMapper.Photo(p.SitePlan),
            [.. p.Reras.Select(r => new ReraDto(r.Number, r.State, r.RegisteredOn, r.ValidUntil, r.Url))], p.LaunchDate, p.PossessionDate,
            [.. ProjectMapper.Updates(p).Take(12)],
            [.. types.Select(t =>
            {
                var available = units[t.Id].Where(u => u.Status == PropertyStatus.Available).ToList();
                var from = t.PriceOnRequest ? null : available.Where(u => u.PriceValue is not null).Select(u => u.PriceValue).Min();
                return new SharedUnitTypeDto(t.Name, t.Type, t.Bhk, t.Bathrooms, t.Balconies, AreaMath.Dto(t.CarpetArea), AreaMath.Dto(t.BuiltUpArea),
                    AreaMath.Dto(t.SuperArea), AreaMath.Dto(t.PlotArea), ProjectMapper.Photo(t.FloorPlan), from, t.PriceOnRequest, available.Count);
            })],
            s.DealerName, s.DealerPhone, s.ReraNumber, !p.IsDeleted);
    }
}

internal static class ProjectShareMapping
{
    public static ProjectShareDto ToDto(ShareDocument s, ProjectDocument p, ShareOptions options, string? leadName, decimal? fromPrice)
    {
        var url = $"{options.BaseUrl.TrimEnd('/')}/pj/{s.Token}";
        var from = fromPrice is { } v ? $"\nFrom {EstateText.Rupees(v)}" : string.Empty;
        var message = $"Namaste {leadName ?? "ji"}, here is a project for you: {p.Name}, {p.Locality}{from}\nPhotos, brochure and details: {url}";
        return new ProjectShareDto(s.Id, s.Token, url, p.Id, s.RequirementId, s.Opens,
            s.LastOpenedAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
            new DateTimeOffset(DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc)), message, s.Enquiries);
    }

    /// <summary>The lowest price of an available unit whose type does not hide its price.</summary>
    public static async Task<decimal?> FromPriceAsync(ProjectDocument p, IPropertyRepository properties, IUnitTypeRepository unitTypes, CancellationToken ct)
    {
        var hidden = await unitTypes.GetDocuments(t => t.ProjectId == p.Id && t.PriceOnRequest).Select(t => t.Id).ToListAsync(ct);
        return (await properties.GetDocuments(u => u.ProjectId == p.Id && u.Status == PropertyStatus.Available && u.PriceValue != null)
                .Select(u => new { u.UnitTypeId, u.PriceValue }).ToListAsync(ct))
            .Where(u => u.UnitTypeId is null || !hidden.Contains(u.UnitTypeId)).Select(u => u.PriceValue).Min();
    }
}
