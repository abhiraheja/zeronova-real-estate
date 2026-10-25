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

    /// <param name="encryptionKey">Null = Core's master key not set up.</param>
    public TestHost(MongoFixture mongo, string? encryptionKey = "default")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.Runner.ConnectionString,
            ["Mongo:DatabaseName"] = "zeronova-real-estate-tests-" + Guid.NewGuid().ToString("N")[..12],
            ["Messaging:ProjectId"] = "zeronova-tests",
            ["Messaging:EnsureResources"] = "false",
            ["Messaging:Source"] = "zeronova-real-estate-tests",
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

    public async Task EnsureIndexesAsync()
    {
        foreach (var service in provider.GetServices<IHostedService>().OfType<IndexInitializer>())
            await service.StartAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();
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
