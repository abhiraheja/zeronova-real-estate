using System.Security.Cryptography;
using FluentValidation;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Website;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Configuration;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Videos;

namespace ZeroNovaRealEstate.Application.Features.Videos;

// Property and project videos (D36): the files live in zeronova-media (client `real-estate`, account = the company); this
// service keeps one row per video on its listing, decides who may upload and who may watch, and asks zeronova-media for a
// short playback grant. Units have none of their own — they show their project's.

/// <summary>
/// The videos of listings: reading them, keeping them in step with zeronova-media (the status poll, the events),
/// removing them, and playback grants.
/// </summary>
public sealed class EstateVideoBook(IEstateVideoRepository videos, IMediaClient media, MediaOptions options, ILogger<EstateVideoBook> logger)
{
    public void RequireMedia()
    {
        if (!media.IsConfigured)
            throw new ValidatorException("Video is not set up yet.");
    }

    /// <summary>The videos of these listings by listing id, in their order. <paramref name="company"/> null = the caller's.</summary>
    public async Task<ILookup<string, EstateVideoDocument>> OfAsync(EstateListingKind kind, IReadOnlyCollection<string> listingIds, bool readyOnly,
        string? company, CancellationToken ct)
    {
        if (listingIds.Count == 0)
            return Array.Empty<EstateVideoDocument>().ToLookup(v => v.ListingId);
        var ids = listingIds.Distinct().ToList();
        var query = videos.GetDocuments(v => v.ListingKind == kind && ids.Contains(v.ListingId), false, company);
        if (readyOnly)
            query = query.Where(v => v.Status == EstateVideoStatus.Ready);
        return (await query.ToListAsync(ct)).OrderBy(v => v.SortOrder).ThenBy(v => v.AddedAt).ToLookup(v => v.ListingId);
    }

    /// <summary>The ready videos of one listing, as the share page and the website show them.</summary>
    public async Task<IReadOnlyList<EstatePublicVideoDto>> PublicAsync(EstateListingKind kind, string listingId, string company, CancellationToken ct) =>
        [.. (await OfAsync(kind, [listingId], true, company, ct))[listingId].Select(Public)];

    public static EstateVideoDto Dto(EstateVideoDocument v) => new(v.VideoId, v.ListingKind, v.ListingId, v.Title, v.Status, v.FailureReason, v.DurationSeconds,
        v.PosterUrl, v.SortOrder, new DateTimeOffset(DateTime.SpecifyKind(v.AddedAt, DateTimeKind.Utc)));

    public static EstatePublicVideoDto Public(EstateVideoDocument v) => new(v.VideoId, v.Title, v.PosterUrl, v.DurationSeconds);

    /// <summary>A call to zeronova-media from a member's request: a refusal shows its message, a failure a plain one.</summary>
    public static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (MediaRefusedException ex)
        {
            throw new ValidatorException(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ValidatorException("The video service did not answer. Try again in a moment.");
        }
    }

    /// <summary>
    /// Asks zeronova-media about the rows still uploading or processing and writes what changed. A failure is logged and
    /// the rows stay as they were (the events, or the next poll, put them right).
    /// </summary>
    public async Task RefreshAsync(IReadOnlyList<EstateVideoDocument> rows, string company, CancellationToken ct)
    {
        var pending = rows.Where(v => v.Status is EstateVideoStatus.Uploading or EstateVideoStatus.Processing).ToList();
        if (pending.Count == 0 || !media.IsConfigured)
            return;
        IReadOnlyList<MediaVideo> found;
        try
        {
            found = await media.GetVideosAsync(company, [.. pending.Select(v => v.VideoId)], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Asking zeronova-media about {Count} videos of company {CompanyId} failed.", pending.Count, company);
            return;
        }
        var byId = found.GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var row in pending)
        {
            if (!Apply(row, byId.GetValueOrDefault(row.VideoId)))
                continue;
            try
            {
                await videos.UpdateAsync(row, ct, company);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Changed at the same moment (an event): the next read shows it.
                logger.LogInformation(ex, "Video {VideoId} not refreshed.", row.VideoId);
            }
        }
    }

    /// <summary>Copies zeronova-media's state onto a row still uploading or processing; true when something changed.</summary>
    public static bool Apply(EstateVideoDocument row, MediaVideo? video)
    {
        switch (video?.Status?.ToLowerInvariant())
        {
            case "ready":
                return MarkReady(row, video.DurationSeconds, video.PosterUrl);
            case "failed":
                return MarkFailed(row, video.FailureReason, video.DurationSeconds);
            case "processing":
                if (row.Status == EstateVideoStatus.Processing)
                    return false;
                row.Status = EstateVideoStatus.Processing;
                return true;
            case "uploading":
                return false;
            default:
                // Deleted there, or never seen: an upload left idle for a day is dropped by zeronova-media.
                return MarkFailed(row, "The upload did not finish. Delete this video and upload it again.", null);
        }
    }

    /// <summary>Ready — unless it is longer than 2 minutes after all (D36 checks again on ready).</summary>
    public static bool MarkReady(EstateVideoDocument row, double? durationSeconds, string? posterUrl)
    {
        if (durationSeconds is > EstateVideoRules.MaxDurationSeconds + 0.5)
            return MarkFailed(row, EstateVideoRules.TooLong, durationSeconds);
        if (row.Status == EstateVideoStatus.Ready && row.DurationSeconds == durationSeconds && row.PosterUrl == posterUrl)
            return false;
        row.Status = EstateVideoStatus.Ready;
        row.FailureReason = null;
        row.DurationSeconds = durationSeconds ?? row.DurationSeconds;
        row.PosterUrl = posterUrl ?? row.PosterUrl;
        return true;
    }

    public static bool MarkFailed(EstateVideoDocument row, string? reason, double? durationSeconds)
    {
        var why = string.IsNullOrWhiteSpace(reason) ? "The video could not be prepared." : reason.Trim();
        if (row.Status == EstateVideoStatus.Failed && row.FailureReason == why)
            return false;
        row.Status = EstateVideoStatus.Failed;
        row.FailureReason = why;
        row.DurationSeconds = durationSeconds ?? row.DurationSeconds;
        return true;
    }

    /// <summary>
    /// Removes a video: the row now, the file in zeronova-media as well as it can (an upload in progress is aborted; a
    /// failure is logged — media's sweeps drop abandoned uploads).
    /// </summary>
    public async Task RemoveAsync(EstateVideoDocument row, string company, CancellationToken ct)
    {
        await videos.DeleteAsync(row.Id, ct, company);
        await DropInMediaAsync(row, company, ct);
    }

    /// <summary>Every video of a listing that is being removed (best effort in zeronova-media).</summary>
    public async Task RemoveForListingAsync(EstateListingKind kind, string listingId, string company, CancellationToken ct)
    {
        foreach (var row in (await OfAsync(kind, [listingId], false, company, ct))[listingId])
            await RemoveAsync(row, company, ct);
    }

    private async Task DropInMediaAsync(EstateVideoDocument row, string company, CancellationToken ct)
    {
        if (!media.IsConfigured)
        {
            logger.LogWarning("Video {VideoId} of company {CompanyId} not deleted in zeronova-media: Media is not set up.", row.VideoId, company);
            return;
        }
        try
        {
            if (row.Status == EstateVideoStatus.Uploading)
                await media.AbortUploadAsync(company, row.VideoId, ct);
            else
                await media.DeleteVideoAsync(company, row.VideoId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Deleting video {VideoId} of company {CompanyId} in zeronova-media failed.", row.VideoId, company);
        }
    }

    /// <summary>A playback grant for one viewer of a ready video.</summary>
    public async Task<EstateVideoGrantDto> GrantAsync(EstateVideoDocument row, string company, string viewerRef, CancellationToken ct)
    {
        RequireMedia();
        if (row.Status != EstateVideoStatus.Ready)
            throw new ValidatorException("This video is not ready yet.");
        var ttl = Math.Clamp(options.GrantSeconds, 60, 900);
        var grant = await CallAsync(() => media.GrantAsync(company, row.VideoId, viewerRef, ttl, ct));
        return new EstateVideoGrantDto(row.VideoId, grant.PlaybackUrl, grant.PosterUrl ?? row.PosterUrl, grant.DurationSeconds ?? row.DurationSeconds, grant.ExpiresAt);
    }

    /// <summary>
    /// A random id per page view: zeronova-media limits key requests per viewer (60 a minute a pod), so a share page or
    /// website shared by many visitors needs a ref per visit.
    /// </summary>
    public static string Visit() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
}

/// <summary>Who may change which videos (D39): a property's with PPRM, a project's with PPJM.</summary>
internal static class VideoAccess
{
    public static void RequireManage(EstateScope scope, EstateListingKind kind)
    {
        if (kind == EstateListingKind.Project ? !scope.Has(EstatePermissions.ProjectManage) : !scope.Has(EstatePermissions.PropertyManage))
            throw new ValidatorException(kind == EstateListingKind.Project
                ? "Changing a project's videos needs Manage projects."
                : "Changing a property's videos needs Manage properties.");
    }

    /// <summary>The listing a video may be added to: a property of the stock (not a project unit) or a project.</summary>
    public static async Task RequireListingAsync(IPropertyRepository properties, IProjectRepository projects, EstateListingKind kind, string listingId,
        CancellationToken ct)
    {
        if (kind == EstateListingKind.Project)
        {
            if (!await projects.GetDocument(listingId).AnyAsync(ct))
                throw new ValidatorException("Project not found.");
            return;
        }
        var property = await properties.GetDocument(listingId).Select(p => new { p.ProjectId }).FirstOrDefaultAsync(ct)
            ?? throw new ValidatorException("Property not found.");
        if (property.ProjectId is not null)
            throw new ValidatorException("A unit shows its project's videos. Add the video to the project.");
    }

    /// <summary>A video of the caller's company by zeronova-media's id, that the caller may change.</summary>
    public static async Task<EstateVideoDocument> RequireOwnAsync(IEstateVideoRepository videos, EstateScope scope, string videoId, CancellationToken ct)
    {
        var id = videoId.Trim();
        var row = await videos.GetDocuments(v => v.VideoId == id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Video not found.");
        RequireManage(scope, row.ListingKind);
        return row;
    }
}

// ── Uploading (PPRM for a property, PPJM for a project) ──────────────────────────────────────────────────

/// <summary>
/// Starts a video of a property or project: zeronova-media makes the upload (at most 2 minutes, checked when it encodes)
/// and the row shows it Uploading. At most 3 a listing; a <c>video/*</c> file of at most 1 GB.
/// </summary>
public sealed record StartEstateVideoUploadCommand(EstateListingKind ListingKind, string ListingId, string FileName, long SizeBytes, string ContentType, string? Title)
    : IRequest<EstateVideoUploadDto>;

public sealed class StartEstateVideoUploadValidator : AbstractValidator<StartEstateVideoUploadCommand>
{
    public StartEstateVideoUploadValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().WithMessage("Give the file's name.").MaximumLength(200);
        RuleFor(x => x.SizeBytes).GreaterThan(0).WithMessage("The file is empty.")
            .LessThanOrEqualTo(EstateVideoRules.MaxUploadBytes).WithMessage("A video may be at most 1 GB. Record a shorter or smaller one.");
        RuleFor(x => x.ContentType).Must(t => t is not null && t.Trim().StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Choose a video file (MP4, MOV, MKV, WEBM or AVI).");
        RuleFor(x => x.Title).MaximumLength(EstateVideoRules.TitleLimit);
    }
}

public sealed class StartEstateVideoUploadHandler(IEstateVideoRepository videos, IPropertyRepository properties, IProjectRepository projects, IMediaClient media,
    EstateVideoBook book, EstateScope scope, IRequestContext caller, TimeProvider clock)
    : IRequestHandler<StartEstateVideoUploadCommand, EstateVideoUploadDto>
{
    public async Task<EstateVideoUploadDto> Handle(StartEstateVideoUploadCommand request, CancellationToken ct)
    {
        VideoAccess.RequireManage(scope, request.ListingKind);
        book.RequireMedia();
        var listingId = request.ListingId.Trim();
        await VideoAccess.RequireListingAsync(properties, projects, request.ListingKind, listingId, ct);
        var company = caller.CompanyId!;

        var rows = (await book.OfAsync(request.ListingKind, [listingId], false, company, ct))[listingId].ToList();
        // An upload abandoned long ago turns Failed here, so the dealer sees why the slot is taken.
        await book.RefreshAsync(rows, company, ct);
        if (rows.Count >= EstateVideoRules.MaxPerListing)
            throw new ValidatorException($"A {(request.ListingKind == EstateListingKind.Project ? "project" : "property")} takes at most {EstateVideoRules.MaxPerListing} videos. Delete one first.");

        var fileName = request.FileName.Trim();
        var title = EstateText.Clean(request.Title) ?? EstateText.Clean(System.IO.Path.GetFileNameWithoutExtension(fileName)) ?? "Video";
        if (title.Length > EstateVideoRules.TitleLimit)
            title = title[..EstateVideoRules.TitleLimit];
        var externalRef = EstateVideoDocument.RefOf(request.ListingKind, listingId);
        var upload = await EstateVideoBook.CallAsync(() => media.CreateVideoAsync(company, title, externalRef, fileName, request.SizeBytes,
            request.ContentType.Trim().ToLowerInvariant(), EstateVideoRules.MaxDurationSeconds, ct));

        var row = new EstateVideoDocument
        {
            Id = videos.GetNewId, ListingKind = request.ListingKind, ListingId = listingId, VideoId = upload.VideoId, ExternalRef = externalRef, Title = title,
            Status = EstateVideoStatus.Uploading, SortOrder = rows.Count == 0 ? 0 : rows.Max(r => r.SortOrder) + 1, AddedAt = clock.GetUtcNow().UtcDateTime,
            FileName = fileName, SizeBytes = request.SizeBytes,
        };
        await videos.AddAsync(row, ct, company);
        return UploadDto(upload, row);
    }

    internal static EstateVideoUploadDto UploadDto(MediaUpload upload, EstateVideoDocument row) =>
        new(upload.VideoId, upload.PartSize, upload.PartCount, [.. upload.Parts.Select(p => new EstateVideoPartDto(p.Number, p.Url))], upload.ExpiresAt,
            EstateVideoBook.Dto(row));
}

/// <summary>More (or renewed) part URLs for an upload in progress.</summary>
public sealed record MoreEstateVideoPartsCommand(string VideoId, IReadOnlyList<int> Numbers) : IRequest<EstateVideoUploadDto>;

public sealed class MoreEstateVideoPartsValidator : AbstractValidator<MoreEstateVideoPartsCommand>
{
    public MoreEstateVideoPartsValidator()
    {
        RuleFor(x => x.VideoId).NotEmpty();
        RuleFor(x => x.Numbers).NotEmpty().Must(n => n.Count <= 100).WithMessage("Ask for at most 100 parts at a time.");
    }
}

public sealed class MoreEstateVideoPartsHandler(IEstateVideoRepository videos, IMediaClient media, EstateVideoBook book, EstateScope scope, IRequestContext caller)
    : IRequestHandler<MoreEstateVideoPartsCommand, EstateVideoUploadDto>
{
    public async Task<EstateVideoUploadDto> Handle(MoreEstateVideoPartsCommand request, CancellationToken ct)
    {
        book.RequireMedia();
        var row = await VideoAccess.RequireOwnAsync(videos, scope, request.VideoId, ct);
        if (row.Status != EstateVideoStatus.Uploading)
            throw new ValidatorException("This video is already uploaded.");
        var upload = await EstateVideoBook.CallAsync(() => media.PartUrlsAsync(caller.CompanyId!, row.VideoId, request.Numbers, ct));
        return StartEstateVideoUploadHandler.UploadDto(upload, row);
    }
}

/// <summary>Every part is up: zeronova-media joins them and encodes; the video is Processing until ready (or failed — too long, say).</summary>
public sealed record CompleteEstateVideoUploadCommand(string VideoId, IReadOnlyList<EstateVideoPartInput> Parts) : IRequest<EstateVideoDto>;

public sealed class CompleteEstateVideoUploadValidator : AbstractValidator<CompleteEstateVideoUploadCommand>
{
    public CompleteEstateVideoUploadValidator()
    {
        RuleFor(x => x.VideoId).NotEmpty();
        RuleFor(x => x.Parts).NotEmpty().WithMessage("No part of the video was uploaded.");
        RuleForEach(x => x.Parts).Must(p => p.Number > 0 && !string.IsNullOrWhiteSpace(p.Etag)).WithMessage("Every part needs its number and ETag.");
    }
}

public sealed class CompleteEstateVideoUploadHandler(IEstateVideoRepository videos, IMediaClient media, EstateVideoBook book, EstateScope scope, IRequestContext caller)
    : IRequestHandler<CompleteEstateVideoUploadCommand, EstateVideoDto>
{
    public async Task<EstateVideoDto> Handle(CompleteEstateVideoUploadCommand request, CancellationToken ct)
    {
        book.RequireMedia();
        var row = await VideoAccess.RequireOwnAsync(videos, scope, request.VideoId, ct);
        if (row.Status != EstateVideoStatus.Uploading)
            return EstateVideoBook.Dto(row);
        var company = caller.CompanyId!;
        var video = await EstateVideoBook.CallAsync(() => media.CompleteUploadAsync(company, row.VideoId,
            [.. request.Parts.OrderBy(p => p.Number).Select(p => (p.Number, p.Etag.Trim()))], ct));
        EstateVideoBook.Apply(row, video);
        if (row.Status == EstateVideoStatus.Uploading)
            row.Status = EstateVideoStatus.Processing;
        await videos.UpdateAsync(row, ct, company);
        return EstateVideoBook.Dto(row);
    }
}

/// <summary>Gives up an upload: the row goes, and zeronova-media drops the parts (as well as it can).</summary>
public sealed record CancelEstateVideoUploadCommand(string VideoId) : IRequest<bool>;

public sealed class CancelEstateVideoUploadHandler(IEstateVideoRepository videos, EstateVideoBook book, EstateScope scope, IRequestContext caller)
    : IRequestHandler<CancelEstateVideoUploadCommand, bool>
{
    public async Task<bool> Handle(CancelEstateVideoUploadCommand request, CancellationToken ct)
    {
        var row = await VideoAccess.RequireOwnAsync(videos, scope, request.VideoId, ct);
        if (row.Status != EstateVideoStatus.Uploading)
            throw new ValidatorException("This video is already uploaded. Delete it instead.");
        await book.RemoveAsync(row, caller.CompanyId!, ct);
        return true;
    }
}

/// <summary>Removes a video from its listing, and from zeronova-media (as well as it can; a failure is logged).</summary>
public sealed record DeleteEstateVideoCommand(string VideoId) : IRequest<bool>;

public sealed class DeleteEstateVideoHandler(IEstateVideoRepository videos, EstateVideoBook book, EstateScope scope, IRequestContext caller)
    : IRequestHandler<DeleteEstateVideoCommand, bool>
{
    public async Task<bool> Handle(DeleteEstateVideoCommand request, CancellationToken ct)
    {
        var row = await VideoAccess.RequireOwnAsync(videos, scope, request.VideoId, ct);
        await book.RemoveAsync(row, caller.CompanyId!, ct);
        return true;
    }
}

/// <summary>A video's title (zeronova-media is told too, as well as it can).</summary>
public sealed record RenameEstateVideoCommand(string VideoId, string Title) : IRequest<EstateVideoDto>;

public sealed class RenameEstateVideoValidator : AbstractValidator<RenameEstateVideoCommand>
{
    public RenameEstateVideoValidator()
    {
        RuleFor(x => x.VideoId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().WithMessage("Give the video a title.").MaximumLength(EstateVideoRules.TitleLimit);
    }
}

public sealed class RenameEstateVideoHandler(IEstateVideoRepository videos, IMediaClient media, EstateScope scope, IRequestContext caller,
    ILogger<RenameEstateVideoHandler> logger)
    : IRequestHandler<RenameEstateVideoCommand, EstateVideoDto>
{
    public async Task<EstateVideoDto> Handle(RenameEstateVideoCommand request, CancellationToken ct)
    {
        var row = await VideoAccess.RequireOwnAsync(videos, scope, request.VideoId, ct);
        var title = EstateText.Clean(request.Title) ?? throw new ValidatorException("Give the video a title.");
        if (title == row.Title)
            return EstateVideoBook.Dto(row);
        row.Title = title;
        await videos.UpdateAsync(row, ct);
        if (media.IsConfigured)
        {
            try
            {
                await media.RenameAsync(caller.CompanyId!, row.VideoId, title, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogInformation(ex, "Renaming video {VideoId} in zeronova-media failed (the title here is changed).", row.VideoId);
            }
        }
        return EstateVideoBook.Dto(row);
    }
}

/// <summary>A listing's videos in this order (ids not given keep their place after them).</summary>
public sealed record ReorderEstateVideosCommand(EstateListingKind ListingKind, string ListingId, IReadOnlyList<string> VideoIds) : IRequest<IReadOnlyList<EstateVideoDto>>;

public sealed class ReorderEstateVideosHandler(IEstateVideoRepository videos, IPropertyRepository properties, IProjectRepository projects, EstateVideoBook book,
    EstateScope scope, IRequestContext caller)
    : IRequestHandler<ReorderEstateVideosCommand, IReadOnlyList<EstateVideoDto>>
{
    public async Task<IReadOnlyList<EstateVideoDto>> Handle(ReorderEstateVideosCommand request, CancellationToken ct)
    {
        VideoAccess.RequireManage(scope, request.ListingKind);
        var listingId = request.ListingId.Trim();
        await VideoAccess.RequireListingAsync(properties, projects, request.ListingKind, listingId, ct);
        var company = caller.CompanyId!;
        var rows = (await book.OfAsync(request.ListingKind, [listingId], false, company, ct))[listingId].ToList();
        var wanted = request.VideoIds.Select(id => id.Trim()).Distinct().ToList();
        if (wanted.Any(id => rows.All(r => r.VideoId != id)))
            throw new ValidatorException("A video is not one of this listing's.");
        var ordered = rows.OrderBy(r => wanted.IndexOf(r.VideoId) is var i and >= 0 ? i : wanted.Count + r.SortOrder).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].SortOrder == i)
                continue;
            ordered[i].SortOrder = i;
            await videos.UpdateAsync(ordered[i], ct, company);
        }
        return [.. ordered.Select(EstateVideoBook.Dto)];
    }
}

/// <summary>Videos as they stand now (asked of zeronova-media for those still uploading or processing) — the app's poll.</summary>
public sealed record GetEstateVideoStatusQuery(IReadOnlyList<string> VideoIds) : IRequest<IReadOnlyList<EstateVideoDto>>;

public sealed class GetEstateVideoStatusValidator : AbstractValidator<GetEstateVideoStatusQuery>
{
    public GetEstateVideoStatusValidator() => RuleFor(x => x.VideoIds).Must(ids => ids.Count <= 50).WithMessage("Ask for at most 50 videos at a time.");
}

public sealed class GetEstateVideoStatusHandler(IEstateVideoRepository videos, EstateVideoBook book, IRequestContext caller)
    : IRequestHandler<GetEstateVideoStatusQuery, IReadOnlyList<EstateVideoDto>>
{
    public async Task<IReadOnlyList<EstateVideoDto>> Handle(GetEstateVideoStatusQuery request, CancellationToken ct)
    {
        var ids = request.VideoIds.Select(id => id.Trim()).Where(id => id.Length > 0).Distinct().ToList();
        if (ids.Count == 0)
            return [];
        var rows = await videos.GetDocuments(v => ids.Contains(v.VideoId)).ToListAsync(ct);
        await book.RefreshAsync(rows, caller.CompanyId!, ct);
        return [.. rows.OrderBy(r => ids.IndexOf(r.VideoId)).Select(EstateVideoBook.Dto)];
    }
}

// ── Playback: the app (members), the share page and the website (nobody signed in) ───────────────────────

/// <summary>A ready video for a signed-in member (viewer <c>app:&lt;userId&gt;</c>), the grant's address to play now.</summary>
public sealed record EstateVideoPlaybackCommand(string VideoId) : IRequest<EstateVideoGrantDto>;

public sealed class EstateVideoPlaybackHandler(IEstateVideoRepository videos, EstateVideoBook book, EstateScope scope, IRequestContext caller)
    : IRequestHandler<EstateVideoPlaybackCommand, EstateVideoGrantDto>
{
    public async Task<EstateVideoGrantDto> Handle(EstateVideoPlaybackCommand request, CancellationToken ct)
    {
        var id = request.VideoId.Trim();
        var row = await videos.GetDocuments(v => v.VideoId == id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Video not found.");
        return await book.GrantAsync(row, caller.CompanyId!, $"app:{scope.UserId}", ct);
    }
}

/// <summary>
/// A ready video of a shared property or project, for anyone with the link (viewer <c>share:&lt;shareId&gt;:&lt;visit&gt;</c>).
/// A shared unit plays its project's videos. A link whose listing was removed plays nothing.
/// </summary>
public sealed record EstateShareVideoPlaybackCommand(string Token, string VideoId) : IRequest<EstateVideoGrantDto>;

public sealed class EstateShareVideoPlaybackHandler(IShareRepository shares, IPropertyRepository properties, IProjectRepository projects, IEstateVideoRepository videos,
    EstateVideoBook book)
    : IRequestHandler<EstateShareVideoPlaybackCommand, EstateVideoGrantDto>
{
    public async Task<EstateVideoGrantDto> Handle(EstateShareVideoPlaybackCommand request, CancellationToken ct)
    {
        var share = await shares.FindByTokenAnyTenantAsync(request.Token.Trim(), ct) ?? throw new ValidatorException("This link is not valid.");
        var company = share.CompanyId;
        var (kind, listingId) = await ListingAsync(share, ct);
        var id = request.VideoId.Trim();
        var row = await videos.GetDocuments(v => v.VideoId == id && v.ListingKind == kind && v.ListingId == listingId, false, company).FirstOrDefaultAsync(ct);
        if (row is not { Status: EstateVideoStatus.Ready })
            throw new ValidatorException("This video is not available.");
        return await book.GrantAsync(row, company, $"share:{share.Id}:{EstateVideoBook.Visit()}", ct);
    }

    private async Task<(EstateListingKind Kind, string Id)> ListingAsync(ShareDocument share, CancellationToken ct)
    {
        if (share.ProjectId is { } projectId)
            return await projects.GetDocument(projectId, false, share.CompanyId).AnyAsync(ct)
                ? (EstateListingKind.Project, projectId)
                : throw new ValidatorException("This video is not available.");
        if (share.PropertyId is { } propertyId
            && await properties.GetDocument(propertyId, false, share.CompanyId).Select(p => new { p.ProjectId }).FirstOrDefaultAsync(ct) is { } p)
            return p.ProjectId is { } unitProject ? (EstateListingKind.Project, unitProject) : (EstateListingKind.Property, propertyId);
        throw new ValidatorException("This video is not available.");
    }
}

/// <summary>
/// A ready video of a property or project listed on the company's website, by the website's code and the listing's slug
/// (viewer <c>web:&lt;code&gt;:&lt;visit&gt;</c>). Nothing for a listing no longer on the website.
/// </summary>
public sealed record EstateWebsiteVideoPlaybackCommand(string Code, string Slug, string VideoId) : IRequest<EstateVideoGrantDto>;

public sealed class EstateWebsiteVideoPlaybackHandler(IWebsiteRepository websites, IPropertyRepository properties, IProjectRepository projects,
    IEstateSettingsRepository settings, IEstateVideoRepository videos, EstateVideoBook book, TimeProvider clock)
    : IRequestHandler<EstateWebsiteVideoPlaybackCommand, EstateVideoGrantDto>
{
    public async Task<EstateVideoGrantDto> Handle(EstateWebsiteVideoPlaybackCommand request, CancellationToken ct)
    {
        var site = await WebsiteLookup.RequireAsync(websites, request.Code, ct);
        var company = site.CompanyId;
        var slug = request.Slug.Trim().ToLowerInvariant();
        var id = request.VideoId.Trim();
        var row = await videos.GetDocuments(v => v.VideoId == id, false, company).FirstOrDefaultAsync(ct);
        if (row is not { Status: EstateVideoStatus.Ready } || slug.Length == 0 || !await ListedAsync(row, slug, company, ct))
            throw new ValidatorException("This video is not available.");
        return await book.GrantAsync(row, company, $"web:{site.Code}:{EstateVideoBook.Visit()}", ct);
    }

    private async Task<bool> ListedAsync(EstateVideoDocument row, string slug, string company, CancellationToken ct)
    {
        var listingId = row.ListingId;
        if (row.ListingKind == EstateListingKind.Project)
            return await WebsiteListings.Projects(projects, company).AnyAsync(p => p.Id == listingId && p.Slug == slug, ct);
        var property = await properties.GetDocument(listingId, false, company).FirstOrDefaultAsync(ct);
        return property is not null && property.Slug == slug
            && WebsiteListings.IsListed(property, await WebsiteFreshness.LoadAsync(settings, company, clock, ct));
    }
}

// ── zeronova-media's events (video-ready, video-failed) ──────────────────────────────────────────────────

/// <summary>
/// zeronova-media's <c>VideoEvent</c> (topic <c>media-events</c>); field names are media's. Every client's videos come
/// here; only this service's (<c>ClientId</c> = <c>Media:ClientId</c>) are read. <see cref="AccountId"/> is the company.
/// </summary>
public abstract class MediaVideoNotification : ZeroNova_Packages.Messaging.Abstractions.ConsumerNotification
{
    public string VideoId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string? ExternalRef { get; set; }
    public double? DurationSeconds { get; set; }
    public List<string>? Renditions { get; set; }
    public string? PosterUrl { get; set; }

    /// <summary><c>video-failed</c>: why ("The video is longer than 2 minutes.").</summary>
    public string? Reason { get; set; }

    public DateTimeOffset At { get; set; }
    public string? LiveStreamId { get; set; }
}

public sealed class VideoReadyNotification : MediaVideoNotification;

public sealed class VideoFailedNotification : MediaVideoNotification;

/// <summary>
/// Applies a media event to its row: the video by zeronova-media's id in the event's company, its external ref the
/// row's. Idempotent (the same state again changes nothing) and in order (an event older than the last applied is
/// dropped). A row already removed is left alone.
/// </summary>
public sealed class MediaVideoEvents(IEstateVideoRepository videos, MediaOptions options, ILogger<MediaVideoEvents> logger)
{
    public async Task ApplyAsync(MediaVideoNotification n, bool ready, CancellationToken ct)
    {
        if (!string.Equals(n.ClientId, options.ClientId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(n.AccountId) || string.IsNullOrWhiteSpace(n.VideoId))
            return;
        var company = n.AccountId;
        var videoId = n.VideoId;
        var row = await videos.GetDocuments(v => v.VideoId == videoId, false, company).FirstOrDefaultAsync(ct);
        if (row is null)
            return;
        if (!string.IsNullOrWhiteSpace(n.ExternalRef) && n.ExternalRef != row.ExternalRef)
        {
            logger.LogWarning("Media event for video {VideoId} names {ExternalRef}, the row {RowRef}; not applied.", videoId, n.ExternalRef, row.ExternalRef);
            return;
        }
        var at = n.At == default ? (DateTime?)null : n.At.UtcDateTime;
        if (at is not null && row.StatusAt is { } last && at < last)
            return;
        var changed = ready ? EstateVideoBook.MarkReady(row, n.DurationSeconds, n.PosterUrl) : EstateVideoBook.MarkFailed(row, n.Reason, n.DurationSeconds);
        if (!changed)
            return;
        row.StatusAt = at ?? row.StatusAt;
        await videos.UpdateAsync(row, ct, company);
    }
}

public sealed class VideoReadyHandler(MediaVideoEvents events) : INotificationHandler<VideoReadyNotification>
{
    public Task Handle(VideoReadyNotification n, CancellationToken ct) => events.ApplyAsync(n, true, ct);
}

public sealed class VideoFailedHandler(MediaVideoEvents events) : INotificationHandler<VideoFailedNotification>
{
    public Task Handle(VideoFailedNotification n, CancellationToken ct) => events.ApplyAsync(n, false, ct);
}
