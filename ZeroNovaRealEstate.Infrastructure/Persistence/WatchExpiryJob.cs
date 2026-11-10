using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZeroNovaRealEstate.Application.Features.Requirements;

namespace ZeroNovaRealEstate.Infrastructure.Persistence;

/// <summary>Once a day (and at start): watched requirements (D33) whose watch ended are closed, their revived matches dropped.</summary>
public sealed class WatchExpiryJob(IServiceScopeFactory scopes, ILogger<WatchExpiryJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var watched = scope.ServiceProvider.GetRequiredService<WatchedDemand>();
                while (await watched.EndWatchesAsync(stoppingToken) > 0 && !stoppingToken.IsCancellationRequested) { }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ending watched requirements failed; trying again tomorrow.");
            }
            await Task.Delay(Every, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }
}
