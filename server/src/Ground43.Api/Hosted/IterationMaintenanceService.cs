using Ground43.Api.Infrastructure;

namespace Ground43.Api.Hosted;

public sealed class IterationMaintenanceService(IServiceScopeFactory scopes, ILogger<IterationMaintenanceService> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Reconcile before the web server starts accepting requests, including after a missed Friday.
        await RunCycleAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var nextMinute = now.AddTicks(TimeSpan.TicksPerMinute - now.Ticks % TimeSpan.TicksPerMinute);
            await Task.Delay(nextMinute - now, stoppingToken);
            await RunCycleAsync(stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IterationRolloverService>().ReconcileAsync(DateTimeOffset.UtcNow, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Iteration rollover failed; will retry next minute"); }
    }
}
