using HotChocolate.Authorization;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Api.Authorization;
using ZeroNovaRealEstate.Api.Graphql.Base;
using ZeroNovaRealEstate.Application.Features.Videos;
using ZeroNovaRealEstate.Models.Videos;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Api.Graphql;

/// <summary>
/// Property and project videos (C4, D36), up to 2 minutes, kept and streamed by zeronova-media. The app uploads the parts
/// straight to storage; a property's videos are changed with PPRM, a project's with PPJM (checked per video). Playing one
/// is a short grant: members in the app, anyone with a share link, and the company's website by its code and the slug.
/// </summary>
[ExtendObjectType(nameof(Query))]
public sealed class VideoQuery(IMediator mediator) : QueryBase
{
    /// <summary>Videos as they stand now (the app's poll while one is uploading or processing), at most 50.</summary>
    [RequirePermission(P.PropertyView, P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_estateVideoStatus")]
    public Task<IReadOnlyList<EstateVideoDto>> VideoStatus(IReadOnlyList<string> videoIds, CancellationToken ct) =>
        mediator.Send(new GetEstateVideoStatusQuery(videoIds), ct);
}

[ExtendObjectType(nameof(Mutation))]
public sealed class VideoMutation(IMediator mediator) : MutationBase
{
    /// <summary>
    /// A new video of a property (not a unit — units show their project's) or a project: at most 3 a listing, a
    /// <c>video/*</c> file of at most 1 GB, at most 2 minutes (checked when encoded: a longer one turns FAILED). Answers the
    /// part URLs to <c>PUT</c> to.
    /// </summary>
    [RequirePermission(P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_startEstateVideoUpload")]
    public Task<EstateVideoUploadDto> StartVideoUpload(EstateListingKind listingKind, string listingId, string fileName, long sizeBytes, string contentType,
        string? title, CancellationToken ct) =>
        mediator.Send(new StartEstateVideoUploadCommand(listingKind, listingId, fileName, sizeBytes, contentType, title), ct);

    /// <summary>More (or renewed) part URLs for an upload in progress.</summary>
    [RequirePermission(P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_moreEstateVideoParts")]
    public Task<EstateVideoUploadDto> MoreVideoParts(string videoId, IReadOnlyList<int> numbers, CancellationToken ct) =>
        mediator.Send(new MoreEstateVideoPartsCommand(videoId, numbers), ct);

    /// <summary>Every part is up (with each part's ETag): the video goes PROCESSING, then READY or FAILED.</summary>
    [RequirePermission(P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_completeEstateVideoUpload")]
    public Task<EstateVideoDto> CompleteVideoUpload(string videoId, IReadOnlyList<EstateVideoPartInput> parts, CancellationToken ct) =>
        mediator.Send(new CompleteEstateVideoUploadCommand(videoId, parts), ct);

    [RequirePermission(P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_cancelEstateVideoUpload")]
    public Task<bool> CancelVideoUpload(string videoId, CancellationToken ct) => mediator.Send(new CancelEstateVideoUploadCommand(videoId), ct);

    [RequirePermission(P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_deleteEstateVideo")]
    public Task<bool> DeleteVideo(string videoId, CancellationToken ct) => mediator.Send(new DeleteEstateVideoCommand(videoId), ct);

    [RequirePermission(P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_renameEstateVideo")]
    public Task<EstateVideoDto> RenameVideo(string videoId, string title, CancellationToken ct) => mediator.Send(new RenameEstateVideoCommand(videoId, title), ct);

    /// <summary>A listing's videos in this order (those not named keep their place after).</summary>
    [RequirePermission(P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_reorderEstateVideos")]
    public Task<IReadOnlyList<EstateVideoDto>> ReorderVideos(EstateListingKind listingKind, string listingId, IReadOnlyList<string> videoIds, CancellationToken ct) =>
        mediator.Send(new ReorderEstateVideosCommand(listingKind, listingId, videoIds), ct);

    /// <summary>A ready video for a member, in the app: a playback address living about 10 minutes (ask again when it lapses).</summary>
    [RequirePermission(P.PropertyView, P.PropertyManage, P.ProjectManage)]
    [GraphQLName("ZeroNova_estateVideoPlayback")]
    public Task<EstateVideoGrantDto> VideoPlayback(string videoId, CancellationToken ct) => mediator.Send(new EstateVideoPlaybackCommand(videoId), ct);

    /// <summary>A ready video of a shared property or project — anyone with the link; ask when the visitor presses play.</summary>
    [AllowAnonymous]
    [GraphQLName("ZeroNova_estateShareVideoPlayback")]
    public Task<EstateVideoGrantDto> ShareVideoPlayback(string token, string videoId, CancellationToken ct) =>
        mediator.Send(new EstateShareVideoPlaybackCommand(token, videoId), ct);

    /// <summary>A ready video of a property or project listed on the company's website (its code, the listing's slug) — the site's server asks.</summary>
    [AllowAnonymous]
    [GraphQLName("ZeroNova_estateWebsiteVideoPlayback")]
    public Task<EstateVideoGrantDto> WebsiteVideoPlayback(string code, string slug, string videoId, CancellationToken ct) =>
        mediator.Send(new EstateWebsiteVideoPlaybackCommand(code, slug, videoId), ct);
}
