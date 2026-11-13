using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Mongo2Go;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Infrastructure;
using ZeroNovaRealEstate.Infrastructure.Persistence;
using ZeroNovaRealEstate.Models.Events;

namespace ZeroNovaRealEstate.Tests;

/// <summary>One throwaway mongod for the whole run (Mongo2Go); every test gets its own database.</summary>
public sealed class MongoFixture : IDisposable
{
    public MongoDbRunner Runner { get; } = MongoDbRunner.Start(singleNodeReplSet: true);

    public void Dispose() => Runner.Dispose();
}

[CollectionDefinition(Name)]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>
{
    public const string Name = "mongo";
}

/// <summary>
/// The Application and Infrastructure layers as the API wires them — real handlers, validators and repositories on a
/// scratch database — with the caller replaced by <see cref="TestCaller"/> and the agents' bells by <see cref="Bells"/>.
/// Authorization is the API's.
/// </summary>
public sealed class TestHost : IAsyncDisposable
{
    public const string CompanyA = "6a0000000000000000000a01";
    public const string CompanyB = "6a0000000000000000000b02";

    private readonly ServiceProvider provider;

    public TestCaller Caller { get; } = new();

    /// <summary>The bells this service asked zeronova-utility to ring.</summary>
    public FakeBells Bells { get; } = new();

    /// <summary>The people this service asked zeronova-lead to put into the contact book.</summary>
    public FakeContacts Contacts { get; } = new();

    /// <summary>The enquiries this service asked zeronova-lead to record (share page, "Add enquiry").</summary>
    public FakeCaptures Captures { get; } = new();

    /// <summary>The WhatsApp templates this service asked zeronova-social to send (D33).</summary>
    public FakeWhatsApp WhatsApp { get; } = new();

    /// <summary>zeronova-media, as this service calls it for property and project videos.</summary>
    public FakeMedia Media { get; } = new();

    /// <param name="encryptionKey">Null = Core's master key not set up.</param>
    /// <param name="realMedia">The real zeronova-media client with no address or key (video not set up) instead of <see cref="Media"/>.</param>
    /// <summary>This host's own database — dropped when the host is disposed (see <see cref="DisposeAsync"/>).</summary>
    private readonly string connectionString;
    private readonly string databaseName = "zeronova-real-estate-tests-" + Guid.NewGuid().ToString("N")[..12];

    public TestHost(MongoFixture mongo, string? encryptionKey = "default", bool realMedia = false)
    {
        connectionString = mongo.Runner.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.Runner.ConnectionString,
            ["Mongo:DatabaseName"] = databaseName,
            ["Messaging:ProjectId"] = "zeronova-tests",
            ["Messaging:EnsureResources"] = "false",
            ["Messaging:Source"] = "zeronova-real-estate-tests",
            // The C5 paperwork bells start at C5's release (2026-11-11) in production; the tests' tenancies are made today.
            ["Rentals:ComplianceBellsFrom"] = "2026-01-01",
            ["Encryption:Key"] = encryptionKey == "default" ? Convert.ToBase64String(Enumerable.Range(7, 32).Select(i => (byte)i).ToArray()) : encryptionKey,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructureServices(configuration);
        services.AddApplication();
        services.RemoveAll<IRequestContext>();
        services.AddSingleton<IRequestContext>(Caller);
        services.RemoveAll<IAgentNotificationProducer>();
        services.AddSingleton<IAgentNotificationProducer>(Bells);
        services.RemoveAll<IContactSyncProducer>();
        services.AddSingleton<IContactSyncProducer>(Contacts);
        services.RemoveAll<ILeadCaptureProducer>();
        services.AddSingleton<ILeadCaptureProducer>(Captures);
        services.RemoveAll<IWhatsAppTemplateProducer>();
        services.AddSingleton<IWhatsAppTemplateProducer>(WhatsApp);
        if (!realMedia)
        {
            services.RemoveAll<IMediaClient>();
            services.AddSingleton<IMediaClient>(Media);
        }
        provider = services.BuildServiceProvider();
    }

    /// <summary>Sends a request through the mediator pipeline (validation included) as the dealer (every permission).</summary>
    public async Task<T> SendAs<T>(string company, IRequest<T> request)
    {
        Caller.CompanyId = company;
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    /// <summary>As a member of <paramref name="company"/> with only these permission codes (an agent, say).</summary>
    public async Task<T> SendAsMember<T>(string company, string userId, IReadOnlyCollection<string> permissions, IRequest<T> request)
    {
        Caller.CompanyId = company;
        Caller.UserId = userId;
        Caller.Permissions = permissions;
        try
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
        }
        finally
        {
            Caller.UserId = "user-test";
            Caller.Permissions = ["*"];
        }
    }

    /// <summary>Runs code against the services with nobody signed in (a background job).</summary>
    public async Task RunAsJobAsync(Func<IServiceProvider, Task> work)
    {
        Caller.CompanyId = null;
        await using var scope = provider.CreateAsyncScope();
        await work(scope.ServiceProvider);
    }

    /// <summary>Runs code with a company's scope (repositories as the dealer).</summary>
    public async Task WithAsync(string company, Func<IServiceProvider, Task> work)
    {
        Caller.CompanyId = company;
        await using var scope = provider.CreateAsyncScope();
        await work(scope.ServiceProvider);
    }

    /// <summary>Delivers a message to its handlers as the consumer does: nobody signed in.</summary>
    public Task PublishAsync<T>(T notification) where T : INotification =>
        RunAsJobAsync(sp => sp.GetRequiredService<IMediator>().Publish(notification));

    public async Task EnsureIndexesAsync()
    {
        foreach (var service in provider.GetServices<IHostedService>().OfType<IndexInitializer>())
            await service.StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// Drops this host's database, then disposes the host. **Not tidiness — what keeps the run alive**: every test makes
    /// its own database on the one shared mongod, and each collection and index is a file WiredTiger keeps open. Kept,
    /// a full run passed the open-file limit about two minutes in (macOS: WiredTiger error 24, EMFILE), mongod aborted,
    /// and every later test timed out at server selection. Never fails a test: cleanup is best effort.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        try
        {
            await new MongoDB.Driver.MongoClient(connectionString).DropDatabaseAsync(databaseName);
        }
        catch (Exception)
        {
            // The run goes on; a database left behind costs files, not correctness.
        }
    }
}

/// <summary>The caller: a member of <see cref="CompanyId"/>.</summary>
public sealed class TestCaller : IRequestContext
{
    public string? CompanyId { get; set; }
    public bool IsAuthenticated => CompanyId is not null;
    public string? AuthUserId => "auth-test";
    public string? UserId { get; set; } = "user-test";
    public string? Email => "dealer@example.test";
    public IReadOnlyCollection<string> Permissions { get; set; } = ["*"];
}

public sealed class FakeContacts : IContactSyncProducer
{
    public ConcurrentQueue<ContactUpsertRequestedEvent> Sent { get; } = new();

    public Task PublishAsync(ContactUpsertRequestedEvent evt, CancellationToken ct = default)
    {
        Sent.Enqueue(evt);
        return Task.CompletedTask;
    }

    public Task<bool> CancelScheduledAsync(string messageId, CancellationToken ct = default) => Task.FromResult(false);
}

public sealed class FakeCaptures : ILeadCaptureProducer
{
    public ConcurrentQueue<(LeadCapturedEnquiryEvent Event, string ChangedBy)> Sent { get; } = new();

    public Task PublishAsync(LeadCapturedEnquiryEvent evt, string changedBy, CancellationToken ct = default)
    {
        Sent.Enqueue((evt, changedBy));
        return Task.CompletedTask;
    }

    public Task<bool> CancelScheduledAsync(string messageId, CancellationToken ct = default) => Task.FromResult(false);
}

public sealed class FakeWhatsApp : IWhatsAppTemplateProducer
{
    public ConcurrentQueue<WhatsAppFirstTouchRequestedEvent> Sent { get; } = new();

    public Task PublishAsync(WhatsAppFirstTouchRequestedEvent evt, CancellationToken ct = default)
    {
        Sent.Enqueue(evt);
        return Task.CompletedTask;
    }

    public Task<bool> CancelScheduledAsync(string messageId, CancellationToken ct = default) => Task.FromResult(false);
}

public sealed class FakeBells : IAgentNotificationProducer
{
    public ConcurrentQueue<DispatchEventMessage> Sent { get; } = new();

    public Task PublishAsync(DispatchEventMessage evt, CancellationToken ct = default)
    {
        Sent.Enqueue(evt);
        return Task.CompletedTask;
    }

    public Task<bool> CancelScheduledAsync(string messageId, CancellationToken ct = default) => Task.FromResult(false);
}

/// <summary>zeronova-media in memory: videos by id, and every create, part request, completion, grant and delete asked for.</summary>
public sealed class FakeMedia : IMediaClient
{
    public ConcurrentDictionary<string, MediaVideo> Videos { get; } = new();
    public ConcurrentQueue<(string Company, string Title, string ExternalRef, long Size, string ContentType, int MaxDuration)> Created { get; } = new();
    public ConcurrentQueue<(string Company, string VideoId, int Parts)> Completed { get; } = new();
    public ConcurrentQueue<(string Company, string VideoId, string Viewer, int Ttl)> Grants { get; } = new();
    public ConcurrentQueue<(string Company, string VideoId)> Deleted { get; } = new();
    public ConcurrentQueue<(string Company, string VideoId)> Aborted { get; } = new();
    public ConcurrentQueue<(string Company, string VideoId, string Title)> Renamed { get; } = new();

    /// <summary>Set to make every delete fail (best-effort deletes must not fail the caller).</summary>
    public bool FailDeletes { get; set; }

    public bool IsConfigured { get; set; } = true;

    private int next;

    public Task<MediaUpload> CreateVideoAsync(string companyId, string title, string externalRef, string fileName, long sizeBytes, string contentType,
        int maxDurationSeconds, CancellationToken ct = default)
    {
        var id = "vid" + Interlocked.Increment(ref next).ToString("D3");
        Videos[id] = new MediaVideo(id, "Uploading", null, null, null);
        Created.Enqueue((companyId, title, externalRef, sizeBytes, contentType, maxDurationSeconds));
        var count = (int)Math.Max(1, (sizeBytes + (16L << 20) - 1) / (16L << 20));
        return Task.FromResult(Upload(id, Enumerable.Range(1, Math.Min(count, 50)), count));
    }

    public Task<MediaUpload> PartUrlsAsync(string companyId, string videoId, IReadOnlyList<int> numbers, CancellationToken ct = default) =>
        Task.FromResult(Upload(videoId, numbers, numbers.Max()));

    public Task<MediaVideo> CompleteUploadAsync(string companyId, string videoId, IReadOnlyList<(int Number, string ETag)> parts, CancellationToken ct = default)
    {
        if (!Videos.TryGetValue(videoId, out var video) || video.Status != "Uploading")
            throw new MediaRefusedException("The video is not being uploaded.");
        Completed.Enqueue((companyId, videoId, parts.Count));
        return Task.FromResult(Videos[videoId] = video with { Status = "Processing" });
    }

    public Task AbortUploadAsync(string companyId, string videoId, CancellationToken ct = default)
    {
        Aborted.Enqueue((companyId, videoId));
        Videos.TryRemove(videoId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MediaVideo>> GetVideosAsync(string companyId, IReadOnlyCollection<string> videoIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MediaVideo>>([.. videoIds.Select(id => Videos.GetValueOrDefault(id)).OfType<MediaVideo>()]);

    public Task RenameAsync(string companyId, string videoId, string title, CancellationToken ct = default)
    {
        Renamed.Enqueue((companyId, videoId, title));
        return Task.CompletedTask;
    }

    public Task DeleteVideoAsync(string companyId, string videoId, CancellationToken ct = default)
    {
        if (FailDeletes)
            throw new HttpRequestException("zeronova-media is down");
        Deleted.Enqueue((companyId, videoId));
        Videos.TryRemove(videoId, out _);
        return Task.CompletedTask;
    }

    public Task<MediaGrant> GrantAsync(string companyId, string videoId, string viewerRef, int ttlSeconds, CancellationToken ct = default)
    {
        if (!Videos.TryGetValue(videoId, out var video) || video.Status != "Ready")
            throw new MediaRefusedException("The video is not ready.");
        Grants.Enqueue((companyId, videoId, viewerRef, ttlSeconds));
        return Task.FromResult(new MediaGrant($"https://media.test/media/v1/play/{videoId}/master.m3u8?t=ticket", video.PosterUrl, video.DurationSeconds,
            DateTimeOffset.UtcNow.AddSeconds(ttlSeconds)));
    }

    /// <summary>As the encoder would: the video ready (or failed) in media.</summary>
    public void Finish(string videoId, string status = "Ready", double? duration = 95, string? reason = null) =>
        Videos[videoId] = new MediaVideo(videoId, status, reason, duration, status == "Ready" ? $"https://video.test/{videoId}/poster.jpg" : null);

    private static MediaUpload Upload(string id, IEnumerable<int> numbers, int count) =>
        new(id, 16L << 20, count, [.. numbers.Select(n => (n, $"https://r2.test/{id}/part{n}?sig=x"))], DateTimeOffset.UtcNow.AddHours(6));
}
