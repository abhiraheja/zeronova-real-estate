using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZeroNovaRealEstate.Application.Features.Properties;

namespace ZeroNovaRealEstate.Infrastructure.Persistence;

/// <summary>Every ten minutes: properties whose hold ran out go back to available and are matched again.</summary>
public sealed class HoldReleaseJob(IServiceScopeFactory scopes, ILogger<HoldReleaseJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var release = scope.ServiceProvider.GetRequiredService<HoldRelease>();
                while (await release.RunOnceAsync(stoppingToken) > 0 && !stoppingToken.IsCancellationRequested) { }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Releasing ended holds failed; trying again in ten minutes.");
            }
            await Task.Delay(Every, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }
}
