using System.Text.Json;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ground43.Api.Infrastructure;

public sealed class CommentNotificationService(AppDbContext db, IWeComNotifier notifier, IOptions<PlatformOptions> options,
    ILogger<CommentNotificationService> logger)
{
    internal const string NotificationType = "comment-mention";
    private sealed record Payload(Guid CommentId, string RequirementId, string UserId, string WeComUserId, string Message);
    public void Enqueue(CommentEntity comment, RequirementEntity requirement, UserEntity author, IEnumerable<UserEntity> recipients)
    {
        static string Short(string value, int max) => value.Length > max ? value[..max] + "…" : value;
        var message = $"【评论提到你】\n{Short(author.Name, 60)} 在评论中 @ 了你\n{requirement.Id} · {Short(requirement.Title, 160)}"
            + $"\n{Short(comment.Content.Trim(), 300)}\n查看评论：{options.Value.PublicBaseUrl.TrimEnd('/')}/?requirement={Uri.EscapeDataString(requirement.Id)}&comment={comment.Id}";
        foreach (var user in recipients.DistinctBy(x => x.Id))
            db.NotificationLogs.Add(new NotificationLogEntity { Id = Guid.NewGuid(), Type = NotificationType, Recipient = user.Id,
                Subject = "评论提到你", IdempotencyKey = $"{NotificationType}:{comment.Id}:{user.Id}", CreatedAt = comment.CreatedAt,
                PayloadJson = JsonSerializer.Serialize(new Payload(comment.Id, requirement.Id, user.Id, user.WeComUserId!, message)) });
    }
    // Called under the existing notification worker's lease. Failures survive restarts.
    public async Task DispatchAsync(CancellationToken ct)
    {
        if (!notifier.IsConfigured) return;
        var candidates = await db.NotificationLogs.AsNoTracking().Where(x => x.Type == NotificationType
            && (x.State == "pending" || x.State == "failed" || x.State == "sending") && x.Attempts < 4).ToListAsync(ct);
        foreach (var log in candidates.OrderBy(x => x.CreatedAt).Take(10))
        {
            var payload = JsonSerializer.Deserialize<Payload>(log.PayloadJson)!;
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == payload.UserId, ct);
            if (user is null || !user.IsActive || user.WeComUserId != payload.WeComUserId
                || !await db.Comments.AnyAsync(x => x.Id == payload.CommentId && x.RequirementId == payload.RequirementId, ct))
            {
                await db.NotificationLogs.Where(x => x.Id == log.Id && x.State != "sent")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled"), ct);
                continue;
            }
            var claimed = await db.NotificationLogs.Where(x => x.Id == log.Id && x.State == log.State && x.Attempts == log.Attempts)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "sending").SetProperty(x => x.Attempts, x => x.Attempts + 1), ct);
            if (claimed == 0) continue;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await notifier.SendToUserAsync(payload.WeComUserId, payload.Message, timeout.Token);
                await db.NotificationLogs.Where(x => x.Id == log.Id && x.State == "sending")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "sent").SetProperty(x => x.SentAt, DateTimeOffset.UtcNow)
                        .SetProperty(x => x.Error, (string?)null), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Comment notification {NotificationId} failed", log.Id);
                await db.NotificationLogs.Where(x => x.Id == log.Id && x.State == "sending")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "failed").SetProperty(x => x.Error, ex.Message), ct);
            }
        }
    }
}
