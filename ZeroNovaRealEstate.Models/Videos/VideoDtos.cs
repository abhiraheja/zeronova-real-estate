namespace ZeroNovaRealEstate.Models.Videos;

/// <summary>What a video belongs to (D36). A project unit has no videos of its own: it shows its project's.</summary>
public enum EstateListingKind
{
    Property,
    Project,
}

/// <summary>
/// A video's state: <see cref="Uploading"/> (the app is sending the parts), <see cref="Processing"/> (zeronova-media is
/// encoding it), <see cref="Ready"/> (plays), <see cref="Failed"/> (with the reason — e.g. "The video is longer than 2 minutes.").
/// </summary>
public enum EstateVideoStatus
{
    Uploading,
    Processing,
    Ready,
    Failed,
}

/// <summary>
/// The rules of property and project videos (D36): at most <see cref="MaxPerListing"/> a listing (failed ones count until
/// deleted), each at most <see cref="MaxDurationSeconds"/> long (zeronova-media checks it when it encodes) and
/// <see cref="MaxUploadBytes"/> big (a 2-minute 1080p phone clip is 100–300 MB), a <c>video/*</c> file.
/// </summary>
public static class EstateVideoRules
{
    public const int MaxPerListing = 3;
    public const int MaxDurationSeconds = 120;
    public const long MaxUploadBytes = 1L << 30;
    public const int TitleLimit = 200;
    public const string TooLong = "The video is longer than 2 minutes.";
}

/// <summary>A video of a property or project as members see it. <see cref="VideoId"/> is zeronova-media's id.</summary>
public sealed record EstateVideoDto(
    string VideoId,
    EstateListingKind ListingKind,
    string ListingId,
    string Title,
    EstateVideoStatus Status,
    string? FailureReason,
    double? DurationSeconds,
    string? PosterUrl,
    int SortOrder,
    DateTimeOffset AddedAt);

/// <summary>
/// A ready video as the share page and the website show it: the id to ask a playback grant for, the title, the poster and
/// the length — never a media key or address that plays.
/// </summary>
public sealed record EstatePublicVideoDto(string Id, string Title, string? PosterUrl, double? DurationSeconds);

public sealed record EstateVideoPartDto(int Number, string Url);

/// <summary>
/// An upload in progress: the app <c>PUT</c>s each part (<see cref="PartSize"/> bytes, the last one less) to its URL and
/// keeps the answer's <c>ETag</c>; the URLs live until <see cref="ExpiresAt"/> (more or renewed ones: <c>moreEstateVideoParts</c>).
/// </summary>
public sealed record EstateVideoUploadDto(
    string VideoId, long PartSize, int PartCount, IReadOnlyList<EstateVideoPartDto> Parts, DateTimeOffset ExpiresAt, EstateVideoDto Video);

public sealed record EstateVideoPartInput(int Number, string Etag);

/// <summary>A short-lived playback address (HLS, encrypted; hls.js on the web, the app's player) for one viewer.</summary>
public sealed record EstateVideoGrantDto(string VideoId, string PlaybackUrl, string? PosterUrl, double? DurationSeconds, DateTimeOffset ExpiresAt);
