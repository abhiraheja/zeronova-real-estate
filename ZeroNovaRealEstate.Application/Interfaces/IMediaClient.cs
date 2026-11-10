namespace ZeroNovaRealEstate.Application.Interfaces;

/// <summary>An upload session in zeronova-media: the parts' presigned URLs.</summary>
public sealed record MediaUpload(string VideoId, long PartSize, int PartCount, IReadOnlyList<(int Number, string Url)> Parts, DateTimeOffset ExpiresAt);

/// <summary>
/// A video as zeronova-media reports it. <paramref name="Status"/>: <c>Uploading</c>, <c>Processing</c>, <c>Ready</c>,
/// <c>Failed</c>, <c>Deleted</c>.
/// </summary>
public sealed record MediaVideo(string Id, string Status, string? FailureReason, double? DurationSeconds, string? PosterUrl);

public sealed record MediaGrant(string PlaybackUrl, string? PosterUrl, double? DurationSeconds, DateTimeOffset ExpiresAt);

/// <summary>Something zeronova-media refused (bad input, not found, not ready): its message is shown as it is.</summary>
public sealed class MediaRefusedException(string message) : Exception(message);

/// <summary>
/// zeronova-media, the video service, as this service's client (<c>real-estate</c>): every call is for one company (the
/// account). Network failures throw; refusals throw <see cref="MediaRefusedException"/>.
/// </summary>
public interface IMediaClient
{
    /// <summary>False while <c>Media:BaseUrl</c> or <c>Media:ClientKey</c> is not set: video operations are refused, nothing else.</summary>
    bool IsConfigured { get; }

    Task<MediaUpload> CreateVideoAsync(string companyId, string title, string externalRef, string fileName, long sizeBytes, string contentType,
        int maxDurationSeconds, CancellationToken ct = default);

    Task<MediaUpload> PartUrlsAsync(string companyId, string videoId, IReadOnlyList<int> numbers, CancellationToken ct = default);

    Task<MediaVideo> CompleteUploadAsync(string companyId, string videoId, IReadOnlyList<(int Number, string ETag)> parts, CancellationToken ct = default);

    Task AbortUploadAsync(string companyId, string videoId, CancellationToken ct = default);

    Task<IReadOnlyList<MediaVideo>> GetVideosAsync(string companyId, IReadOnlyCollection<string> videoIds, CancellationToken ct = default);

    Task RenameAsync(string companyId, string videoId, string title, CancellationToken ct = default);

    Task DeleteVideoAsync(string companyId, string videoId, CancellationToken ct = default);

    /// <summary>A playback grant with no watermark (viewers of share pages and websites are nobody signed in).</summary>
    Task<MediaGrant> GrantAsync(string companyId, string videoId, string viewerRef, int ttlSeconds, CancellationToken ct = default);
}
