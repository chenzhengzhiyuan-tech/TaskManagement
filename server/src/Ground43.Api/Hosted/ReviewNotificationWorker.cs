using Ground43.Api.Data;
using Ground43.Api.Infrastructure;

namespace Ground43.Api.Hosted;

public sealed class ReviewNotificationWorker(IServiceScopeFactory scopes, ILogger<ReviewNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try { await RunAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Review notification dispatch failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobLock = scope.ServiceProvider.GetRequiredService<DistributedJobLock>();
        var owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        if (!await jobLock.TryAcquireAsync("review-notifications", owner, TimeSpan.FromMinutes(4), ct)) return;
        try
        {
            await scope.ServiceProvider.GetRequiredService<ReviewNotificationService>().DispatchAsync(ct);
            await scope.ServiceProvider.GetRequiredService<AssignmentNotificationService>().DispatchAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await jobLock.ReleaseAsync("review-notifications", owner, CancellationToken.None);
        }
    }
}
