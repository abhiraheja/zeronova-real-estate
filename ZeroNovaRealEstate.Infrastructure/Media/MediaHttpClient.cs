using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Models.Configuration;

namespace ZeroNovaRealEstate.Infrastructure.Media;

/// <summary>
/// <see cref="IMediaClient"/> on zeronova-media's REST API (<c>/media/v1</c>, its docs/media-service.md 13.2):
/// <c>x-client-key</c> (this service's key as client <c>real-estate</c>) and <c>x-account-id</c> (the company). Answers are
/// the <c>{ success, data, errors }</c> envelope; a 4xx is a refusal whose message is shown, anything else a failure.
/// </summary>
public sealed class MediaHttpClient(HttpClient http, MediaOptions options) : IMediaClient
{
    public bool IsConfigured => options.IsConfigured;

    public async Task<MediaUpload> CreateVideoAsync(string companyId, string title, string externalRef, string fileName, long sizeBytes, string contentType,
        int maxDurationSeconds, CancellationToken ct = default) =>
        ToUpload(await SendAsync<Session>(HttpMethod.Post, "videos", companyId,
            new { title, externalRef, fileName, sizeBytes, contentType, maxDurationSeconds }, ct));

    public async Task<MediaUpload> PartUrlsAsync(string companyId, string videoId, IReadOnlyList<int> numbers, CancellationToken ct = default) =>
        ToUpload(await SendAsync<Session>(HttpMethod.Post, $"videos/{Uri.EscapeDataString(videoId)}/upload/parts", companyId, new { numbers }, ct));

    public async Task<MediaVideo> CompleteUploadAsync(string companyId, string videoId, IReadOnlyList<(int Number, string ETag)> parts, CancellationToken ct = default) =>
        ToVideo(await SendAsync<Video>(HttpMethod.Post, $"videos/{Uri.EscapeDataString(videoId)}/upload/complete", companyId,
            new { parts = parts.Select(p => new { number = p.Number, etag = p.ETag }) }, ct));

    public Task AbortUploadAsync(string companyId, string videoId, CancellationToken ct = default) =>
        SendAsync<string>(HttpMethod.Delete, $"videos/{Uri.EscapeDataString(videoId)}/upload", companyId, null, ct);

    public async Task<IReadOnlyList<MediaVideo>> GetVideosAsync(string companyId, IReadOnlyCollection<string> videoIds, CancellationToken ct = default)
    {
        var all = new List<MediaVideo>();
        foreach (var chunk in videoIds.Chunk(100))
        {
            var rows = await SendAsync<List<Video>>(HttpMethod.Get, $"videos?ids={string.Join(',', chunk.Select(Uri.EscapeDataString))}", companyId, null, ct);
            all.AddRange(rows.Select(ToVideo));
        }
        return all;
    }

    public Task RenameAsync(string companyId, string videoId, string title, CancellationToken ct = default) =>
        SendAsync<Video>(HttpMethod.Patch, $"videos/{Uri.EscapeDataString(videoId)}", companyId, new { title }, ct);

    public Task DeleteVideoAsync(string companyId, string videoId, CancellationToken ct = default) =>
        SendAsync<string>(HttpMethod.Delete, $"videos/{Uri.EscapeDataString(videoId)}", companyId, null, ct);

    public async Task<MediaGrant> GrantAsync(string companyId, string videoId, string viewerRef, int ttlSeconds, CancellationToken ct = default)
    {
        var grant = await SendAsync<Grant>(HttpMethod.Post, $"videos/{Uri.EscapeDataString(videoId)}/playback", companyId, new { viewerRef, ttlSeconds }, ct);
        return new MediaGrant(grant.PlaybackUrl, grant.PosterUrl, grant.DurationSeconds, grant.ExpiresAt);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, string companyId, object? body, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new MediaRefusedException("Video is not set up yet.");
        using var request = new HttpRequestMessage(method, new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/media/v1/"), path));
        request.Headers.Add("x-client-key", options.ClientKey.Trim());
        request.Headers.Add("x-account-id", companyId);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);

        using var response = await http.SendAsync(request, ct);
        Envelope<T>? envelope;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json, ct);
        }
        catch (JsonException)
        {
            envelope = null;
        }
        if (response.IsSuccessStatusCode && envelope is { Success: true, Data: not null })
            return envelope.Data;

        var message = envelope?.Errors?.FirstOrDefault() ?? $"zeronova-media answered {(int)response.StatusCode}";
        if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            throw new MediaRefusedException(message);
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static MediaUpload ToUpload(Session s) =>
        new(s.VideoId, s.PartSize, s.PartCount, s.Parts.Select(p => (p.Number, p.Url)).ToList(), s.ExpiresAt);

    private static MediaVideo ToVideo(Video v) => new(v.Id, v.Status, v.FailureReason, v.DurationSeconds, v.PosterUrl);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool Success, T? Data, IReadOnlyList<string>? Errors);

    private sealed record Part(int Number, string Url);

    private sealed record Session(string VideoId, string UploadId, long PartSize, int PartCount, List<Part> Parts, DateTimeOffset ExpiresAt);

    private sealed record Video(string Id, string Status, string? FailureReason, double? DurationSeconds, string? PosterUrl);

    private sealed record Grant(string PlaybackUrl, string? PosterUrl, double? DurationSeconds, DateTimeOffset ExpiresAt);
}
