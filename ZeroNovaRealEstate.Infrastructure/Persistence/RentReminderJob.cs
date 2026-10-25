using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZeroNovaRealEstate.Application.Features.Rentals;

namespace ZeroNovaRealEstate.Infrastructure.Persistence;

/// <summary>Every six hours: a bell to the agent for rents past their due day and tenancies ending within a month, once each.</summary>
public sealed class RentReminderJob(IServiceScopeFactory scopes, ILogger<RentReminderJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RentReminders>().RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Rent reminders failed; trying again in six hours.");
            }
            await Task.Delay(Every, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }
}
