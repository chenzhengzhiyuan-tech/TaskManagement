using System.Text.Json;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ground43.Api.Infrastructure;

public sealed class ReviewNotificationService(AppDbContext db, IWeComNotifier notifier,
    IOptions<PlatformOptions> options, ILogger<ReviewNotificationService> logger)
{
    internal const string NotificationType = "review-request";
    internal const int MaxAttempts = 4; // First attempt plus three retries.
    private sealed record Payload(string RequirementId, string ReviewerId);

    // Called before the requirement's SaveChanges: the business edit and outbox commit together.
    public async Task RecordChangeAsync(RequirementEntity item, string? previousStatus, string? previousReviewer, CancellationToken ct)
    {
        if (item.StatusId == previousStatus && item.ReviewerId == previousReviewer) return;
        var prefix = $"{NotificationType}:{item.Id}:";
        var obsolete = await db.NotificationLogs.Where(x => x.Type == NotificationType
            && x.IdempotencyKey!.StartsWith(prefix) && x.State != "sent" && x.State != "cancelled").ToListAsync(ct);
        foreach (var log in obsolete) log.State = "cancelled";
        if (item.StatusId == "review" && item.ReviewerId is not null) Enqueue(item);
    }

    // Pending notices already wait for binding. This also covers tasks created before this release.
    public async Task OnUserBoundAsync(string userId, CancellationToken ct)
    {
        var items = await db.Requirements.AsNoTracking().Where(x => x.StatusId == "review" && x.ReviewerId == userId).ToListAsync(ct);
        foreach (var item in items)
        {
            var prefix = $"{NotificationType}:{item.Id}:";
            if (!await db.NotificationLogs.AnyAsync(x => x.Type == NotificationType
                && x.IdempotencyKey!.StartsWith(prefix) && x.State != "cancelled", ct)) Enqueue(item);
        }
    }

    private void Enqueue(RequirementEntity item) => db.NotificationLogs.Add(new NotificationLogEntity
    {
        Id = Guid.NewGuid(), Type = NotificationType, Recipient = item.ReviewerId!, Subject = "任务待验收",
        PayloadJson = JsonSerializer.Serialize(new Payload(item.Id, item.ReviewerId!)),
        IdempotencyKey = $"{NotificationType}:{item.Id}:{item.Version}:{item.ReviewerId}", CreatedAt = DateTimeOffset.UtcNow
    });

    public async Task DispatchAsync(CancellationToken ct)
    {
        if (!notifier.IsConfigured) return;
        // A dedicated worker holds a database lease while dispatching. A persisted 'sending' row
        // can be recovered after a process restart without losing the notification.
        var candidates = await db.NotificationLogs.AsNoTracking().Where(x => x.Type == NotificationType
            && (x.State == "pending" || x.State == "failed" || x.State == "sending") && x.Attempts < MaxAttempts).ToListAsync(ct);
        var sentThisCycle = 0;
        foreach (var log in candidates.OrderBy(x => x.CreatedAt))
        {
            if (sentThisCycle >= 10) break;
            var payload = JsonSerializer.Deserialize<Payload>(log.PayloadJson)!;
            var item = await db.Requirements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == payload.RequirementId, ct);
            if (item is null || item.StatusId != "review" || item.ReviewerId != payload.ReviewerId)
            {
                await db.NotificationLogs.Where(x => x.Id == log.Id && x.State != "sent")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled"), ct);
                continue;
            }
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == payload.ReviewerId, ct);
            if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.WeComUserId)) continue;
            var claim = await db.NotificationLogs.Where(x => x.Id == log.Id && x.State == log.State && x.Attempts == log.Attempts)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "sending").SetProperty(x => x.Attempts, x => x.Attempts + 1)
                    .SetProperty(x => x.Recipient, user.WeComUserId), ct);
            if (claim == 0) continue;
            sentThisCycle++;
            var baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
            var title = item.Title.Length > 220 ? item.Title[..220] + "…" : item.Title;
            var content = $"【任务待验收】\n{item.Id} {title}\n该任务单已等待验收：\n{baseUrl}/?requirement={Uri.EscapeDataString(item.Id)}";
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await notifier.SendToUserAsync(user.WeComUserId, content, timeout.Token);
                await db.NotificationLogs.Where(x => x.Id == log.Id && x.State == "sending")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "sent").SetProperty(x => x.SentAt, DateTimeOffset.UtcNow)
                        .SetProperty(x => x.Error, (string?)null), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Review notification {NotificationId} failed on attempt {Attempt}", log.Id, log.Attempts + 1);
                await db.NotificationLogs.Where(x => x.Id == log.Id && x.State == "sending")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "failed").SetProperty(x => x.Error, ex.Message), ct);
            }
        }
    }
}
