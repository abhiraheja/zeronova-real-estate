using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using ZeroNovaRealEstate.Api.Endpoints;
using ZeroNovaRealEstate.Application.Features.Usage;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// The usage door (<c>/internal/usage</c>, zeronova-portal docs/company-insights.md; the Usage page, pricing step 1): off
/// without a key, only the usage key opens it, a company is required, and the answer is the contract's JSON.
/// </summary>
public sealed class UsageEndpointTests
{
    private const string Key = "usage-key-0123456789abcdef";

    private static async Task<(WebApplication App, HttpClient Http, FakeUsage Usage)> StartAsync(string serviceKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Usage:ServiceKey"] = serviceKey });
        builder.Services.AddUsageEndpoint(builder.Configuration);
        var fake = new FakeUsage();
        builder.Services.AddSingleton<IEstateUsageSource>(fake);

        var app = builder.Build();
        app.MapUsageEndpoints();
        await app.StartAsync();
        return (app, app.GetTestClient(), fake);
    }

    private static Task<HttpResponseMessage> Get(HttpClient http, string query, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoints.Route + query);
        if (key is not null) request.Headers.Add(UsageEndpoints.KeyHeader, key);
        return http.SendAsync(request);
    }

    [Fact]
    public async Task Without_a_configured_key_the_route_is_off()
    {
        var (app, http, usage) = await StartAsync("");
        await using var _ = app;

        Assert.Equal(HttpStatusCode.NotFound, (await Get(http, "?companyId=c1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(http, "?companyId=c1", "anything")).StatusCode);
        Assert.Empty(usage.Asked);
    }

    [Fact]
    public async Task Only_the_usage_key_opens_it_and_a_company_is_required()
    {
        var (app, http, usage) = await StartAsync(Key);
        await using var _ = app;

        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(http, "?companyId=c1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(http, "?companyId=c1", Key[..^1])).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(http, "?companyId=c1", Key + "x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Get(http, "", Key)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Get(http, "?companyId=" + new string('x', 65), Key)).StatusCode);
        Assert.Empty(usage.Asked);

        var ok = await Get(http, "?companyId=c1&days=7", Key);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(("c1", (int?)7), Assert.Single(usage.Asked));
        using var json = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("realestate", root.GetProperty("service").GetString());
        var metric = root.GetProperty("metrics")[0];
        Assert.Equal(("overview", "realestate.videos.total", 3, "count"),
            (metric.GetProperty("section").GetString(), metric.GetProperty("key").GetString(), metric.GetProperty("value").GetInt32(),
                metric.GetProperty("unit").GetString()));
        Assert.Equal(JsonValueKind.Null, metric.GetProperty("series").ValueKind);
    }

    private sealed class FakeUsage : IEstateUsageSource
    {
        public List<(string, int?)> Asked { get; } = [];

        public Task<EstateUsageSnapshot> GetAsync(string companyId, int? days, CancellationToken ct = default)
        {
            Asked.Add((companyId, days));
            return Task.FromResult(new EstateUsageSnapshot("realestate", companyId, "2026-11-16", "2026-11-22",
                DateTimeOffset.Parse("2026-11-22T10:00:00Z"), null, [new("overview", "realestate.videos.total", 3, "count", null)]));
        }
    }
}

/// <summary>What the counts mean: kept videos (not failed, not removed) of this company only, their bytes, and listings.</summary>
[Collection(MongoCollection.Name)]
public sealed class EstateUsageTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;

    public Task InitializeAsync()
    {
        host = new TestHost(mongo);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private static BsonDocument Video(string company, string status, long bytes, bool deleted = false, int addedDaysAgo = 1) => new()
    {
        ["companyId"] = company,
        ["isDeleted"] = deleted,
        ["listingKind"] = "Property",
        ["listingId"] = ObjectId.GenerateNewId().ToString(),
        ["videoId"] = Guid.NewGuid().ToString("N"),
        ["externalRef"] = "property:x",
        ["title"] = "Walkthrough",
        ["status"] = status,
        ["sizeBytes"] = bytes,
        ["sortOrder"] = 0,
        ["addedAt"] = DateTime.UtcNow.AddDays(-addedDaysAgo),
    };

    [Fact]
    public async Task Counts_the_videos_a_dealer_keeps_and_nothing_of_another()
    {
        EstateUsageSnapshot? usage = null;
        EstateUsageSnapshot? other = null;
        await host.RunAsJobAsync(async sp =>
        {
            var db = sp.GetRequiredService<IMongoDatabase>();
            await db.GetCollection<BsonDocument>("re_videos").InsertManyAsync(
            [
                Video(CompanyA, "Ready", 1000),
                Video(CompanyA, "Processing", 500),
                Video(CompanyA, "Uploading", 200, addedDaysAgo: 0),
                Video(CompanyA, "Failed", 9999),
                Video(CompanyA, "Ready", 7777, deleted: true),
                Video(CompanyB, "Ready", 4242),
            ]);
            await db.GetCollection<BsonDocument>("re_properties").InsertManyAsync(
            [
                new BsonDocument { ["companyId"] = CompanyA, ["isDeleted"] = false },
                new BsonDocument { ["companyId"] = CompanyA, ["isDeleted"] = false },
                new BsonDocument { ["companyId"] = CompanyA, ["isDeleted"] = true },
                new BsonDocument { ["companyId"] = CompanyB, ["isDeleted"] = false },
            ]);
            await db.GetCollection<BsonDocument>("re_projects").InsertOneAsync(new BsonDocument { ["companyId"] = CompanyA, ["isDeleted"] = false });

            var source = sp.GetRequiredService<IEstateUsageSource>();
            usage = await source.GetAsync(CompanyA, 30);
            other = await source.GetAsync(CompanyB, null);
        });

        double Metric(EstateUsageSnapshot s, string key) => Assert.Single(s.Metrics, m => m.Key == key).Value;
        Assert.Equal("realestate", usage!.Service);
        Assert.Equal(3, Metric(usage, "realestate.videos.total"));
        Assert.Equal(1700, Metric(usage, "realestate.videos.bytes"));
        Assert.Equal(2, Metric(usage, "realestate.properties.total"));
        Assert.Equal(1, Metric(usage, "realestate.projects.total"));
        Assert.NotNull(usage.LastActivityAt);
        Assert.Equal(1, Metric(other!, "realestate.videos.total"));
        Assert.Equal(1, Metric(other!, "realestate.properties.total"));
    }

    [Fact]
    public async Task A_dealer_with_nothing_answers_zeros()
    {
        EstateUsageSnapshot? usage = null;
        await host.RunAsJobAsync(async sp => usage = await sp.GetRequiredService<IEstateUsageSource>().GetAsync(CompanyA, 7));

        Assert.All(usage!.Metrics, m => Assert.Equal(0, m.Value));
        Assert.Null(usage.LastActivityAt);
    }
}
