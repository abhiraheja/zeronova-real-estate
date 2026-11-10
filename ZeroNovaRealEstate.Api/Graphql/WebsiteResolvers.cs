using HotChocolate.Authorization;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Api.Authorization;
using ZeroNovaRealEstate.Api.Graphql.Base;
using ZeroNovaRealEstate.Application.Features.Website;
using ZeroNovaRealEstate.Models.Website;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Api.Graphql;

/// <summary>
/// The company's website (cms doc 06 §12, E2). Its settings — on or off, the public code — are PSEM's. The listings are
/// anonymous, by website code, for the site's server through the graph gateway: only what the share pages show.
/// </summary>
[ExtendObjectType(nameof(Query))]
public sealed class WebsiteQuery(IMediator mediator) : QueryBase
{
    /// <summary>On or off and the code (made on first ask), with how many listings the website shows.</summary>
    [RequirePermission(P.SettingsManage, P.PropertyView, P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_estateWebsiteSettings")]
    public Task<EstateWebsiteSettingsDto> WebsiteSettings(CancellationToken ct) => mediator.Send(new GetEstateWebsiteSettingsQuery(), ct);

    /// <summary>The company as its share pages show it, and the cities and localities its listings are in. Anyone with the code.</summary>
    [AllowAnonymous]
    [GraphQLName("ZeroNova_estateWebsite")]
    public Task<EstateWebsiteDto> Website(string code, CancellationToken ct) => mediator.Send(new GetEstateWebsiteQuery(code), ct);

    /// <summary>The listed properties, filtered and sorted; <paramref name="page"/> from 1, up to 48 a page (12 by default).</summary>
    [AllowAnonymous]
    [GraphQLName("ZeroNova_estateWebsiteProperties")]
    public Task<EstateWebsitePropertyPageDto> WebsiteProperties(string code, EstateWebsitePropertyFilterInput? filter, EstateWebsitePropertySort? sort, int? page,
        int? pageSize, CancellationToken ct) =>
        mediator.Send(new GetEstateWebsitePropertiesQuery(code, filter ?? new EstateWebsitePropertyFilterInput(), sort ?? EstateWebsitePropertySort.Featured,
            page ?? 1, pageSize ?? 12), ct);

    /// <summary>A property page by its slug: listed, or no longer available with similar ones.</summary>
    [AllowAnonymous]
    [GraphQLName("ZeroNova_estateWebsiteProperty")]
    public Task<EstateWebsitePropertyResultDto> WebsiteProperty(string code, string slug, CancellationToken ct) =>
        mediator.Send(new GetEstateWebsitePropertyQuery(code, slug), ct);

    [AllowAnonymous]
    [GraphQLName("ZeroNova_estateWebsiteProjects")]
    public Task<IReadOnlyList<EstateWebsiteProjectSummaryDto>> WebsiteProjects(string code, CancellationToken ct) =>
        mediator.Send(new GetEstateWebsiteProjectsQuery(code), ct);

    /// <summary>A listed project by its slug; null when it is not (or no longer) on the website.</summary>
    [AllowAnonymous]
    [GraphQLName("ZeroNova_estateWebsiteProject")]
    public Task<EstateWebsiteProjectDto?> WebsiteProject(string code, string slug, CancellationToken ct) =>
        mediator.Send(new GetEstateWebsiteProjectQuery(code, slug), ct);
}

[ExtendObjectType(nameof(Mutation))]
public sealed class WebsiteMutation(IMediator mediator) : MutationBase
{
    /// <summary>The website on or off, or another code (unique across companies). Null keeps.</summary>
    [RequirePermission(P.SettingsManage)]
    [GraphQLName("ZeroNova_saveEstateWebsiteSettings")]
    public Task<EstateWebsiteSettingsDto> SaveWebsiteSettings(EstateWebsiteSettingsInput input, CancellationToken ct) =>
        mediator.Send(new SaveEstateWebsiteSettingsCommand(input), ct);
}
