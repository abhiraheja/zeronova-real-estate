using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZeroNovaRealEstate.Application.Features.Visits;

namespace ZeroNovaRealEstate.Infrastructure.Persistence;

/// <summary>Every five minutes: a bell to the agent for each site visit starting within the hour, once.</summary>
public sealed class VisitReminderJob(IServiceScopeFactory scopes, ILogger<VisitReminderJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<VisitReminders>().RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Visit reminders failed; trying again in five minutes.");
            }
            await Task.Delay(Every, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }
}
