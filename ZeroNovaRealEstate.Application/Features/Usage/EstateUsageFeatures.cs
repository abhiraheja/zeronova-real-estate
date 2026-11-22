using System.Globalization;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Models.Videos;

namespace ZeroNovaRealEstate.Application.Features.Usage;

/// <summary>One figure of the usage contract (zeronova-portal docs/company-insights.md). A total has no series.</summary>
public sealed record EstateUsageMetric(string Section, string Key, double Value, string Unit, IReadOnlyList<EstateUsagePoint>? Series);

public sealed record EstateUsagePoint(string Day, double Value);

/// <summary>The contract's answer for one company.</summary>
public sealed record EstateUsageSnapshot(
    string Service, string CompanyId, string From, string To, DateTimeOffset GeneratedAt, DateTimeOffset? LastActivityAt,
    IReadOnlyList<EstateUsageMetric> Metrics);

/// <summary>What the usage route asks (an interface so the route can be tested without a database).</summary>
public interface IEstateUsageSource
{
    Task<EstateUsageSnapshot> GetAsync(string companyId, int? days, CancellationToken ct = default);
}

/// <summary>
/// One dealer's counts for Core: the portal's company dashboard and the company's own Usage page (pricing step 1). The pricing
/// sheet sells "200 property videos" with the RealEstate module, so <c>realestate.videos.total</c> counts the videos a
/// company keeps — of properties and projects, uploading, processing or ready; a failed or removed one is not kept. Asked
/// with nobody signed in, so every read names the company itself. Counts only: no title, address or file leaves here.
/// </summary>
public sealed class EstateUsageService(
    IEstateVideoRepository videos, IPropertyRepository properties, IProjectRepository projects, TimeProvider clock)
    : IEstateUsageSource
{
    public const string Service = "realestate";
    public const int DefaultDays = 30;
    public const int MaxDays = 90;

    public async Task<EstateUsageSnapshot> GetAsync(string companyId, int? days, CancellationToken ct = default)
    {
        var window = Math.Clamp(days ?? DefaultDays, 1, MaxDays);
        var now = clock.GetUtcNow();
        var to = DateOnly.FromDateTime(now.UtcDateTime);
        var from = to.AddDays(1 - window);

        var kept = videos.GetDocuments(v => v.Status != EstateVideoStatus.Failed, companyId: companyId);
        var videoCount = await kept.LongCountAsync(ct);
        var videoBytes = videoCount == 0 ? 0 : await kept.SumAsync(v => v.SizeBytes, ct);
        var propertyCount = await properties.GetDocuments(companyId: companyId).LongCountAsync(ct);
        var projectCount = await projects.GetDocuments(companyId: companyId).LongCountAsync(ct);
        var lastAdded = await videos.GetDocuments(companyId: companyId).OrderByDescending(v => v.AddedAt)
            .Select(v => (DateTime?)v.AddedAt).FirstOrDefaultAsync(ct);

        EstateUsageMetric Total(string section, string key, double value, string unit = "count") => new(section, key, value, unit, null);

        return new EstateUsageSnapshot(
            Service, companyId, Day(from), Day(to), now,
            lastAdded is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
            [
                Total("overview", "realestate.videos.total", videoCount),
                Total("storage", "realestate.videos.bytes", videoBytes, "bytes"),
                Total("overview", "realestate.properties.total", propertyCount),
                Total("overview", "realestate.projects.total", projectCount),
            ]);
    }

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
