using System.Text.Json;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ground43.Api.Infrastructure;

public sealed class AssignmentNotificationService(AppDbContext db, IWeComNotifier notifier,
    IOptions<PlatformOptions> options, ILogger<AssignmentNotificationService> logger)
{
    internal const string NotificationType = "assignment";
    private sealed record Payload(string RequirementId, string AssigneeId, string Message);

    // Enqueue in the same transaction as the requirement; never send before saving it.
    public Task RecordChangeAsync(RequirementEntity item, string? previousAssignee, string actorId, bool created, CancellationToken ct)
        => RecordAssigneesChangeAsync(item, previousAssignee is null ? [] : [previousAssignee], actorId, created, ct);

    public async Task RecordAssigneesChangeAsync(RequirementEntity item, string[] previousAssignees, string actorId, bool created, CancellationToken ct)
    {
        var current = item.GetAssigneeIds();
        if (!created && current.ToHashSet().SetEquals(previousAssignees)) return;
        var prefix = $"assignment:{item.Id}:";
        var obsolete = await db.NotificationLogs.Where(x => x.Type == NotificationType
            && x.IdempotencyKey!.StartsWith(prefix) && x.State != "sent" && x.State != "cancelled").ToListAsync(ct);
        foreach (var log in obsolete)
            if (!current.Contains(JsonSerializer.Deserialize<Payload>(log.PayloadJson)!.AssigneeId)) log.State = "cancelled";
        var added = created ? current : current.Except(previousAssignees).ToArray();
        if (added.Length == 0) return;
        var actor = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actorId, ct);
        var previousName = previousAssignees.Length == 0 ? "未分配"
            : string.Join("、", await db.Users.Where(x => previousAssignees.Contains(x.Id)).Select(x => x.Name).ToArrayAsync(ct));
        foreach (var user in await db.Users.AsNoTracking().Where(x => added.Contains(x.Id)).ToListAsync(ct))
        {
        static string Short(string text, int max) => text.Length > max ? text[..max] + "…" : text;
        var priority = item.Priority switch { "urgent" => "极高", "high" => "高", "medium" => "中", _ => "低" };
        var message = $"【{(created ? "新任务" : "任务指派")}】\n{Short(user.Name, 40)}，{(created ? "你收到一条新任务。" : "一条任务已指派给你。")}"
            + $"\n{item.Id} · {Short(item.Title, 160)}\n优先级：{priority}\n期望完成：{item.DueDate?.ToString("yyyy-MM-dd") ?? "未设置"}"
            + (created ? $"\n创建人：{Short(actor.Name, 40)}" : $"\n原处理人：{Short(previousName, 40)}\n操作人：{Short(actor.Name, 40)}")
            + $"\n查看任务：{options.Value.PublicBaseUrl.TrimEnd('/')}/?requirement={Uri.EscapeDataString(item.Id)}";
        db.NotificationLogs.Add(new NotificationLogEntity
        {
            Id = Guid.NewGuid(), Type = NotificationType, Recipient = user.Id,
            Subject = created ? "新任务" : "任务指派", State = "pending",
            PayloadJson = JsonSerializer.Serialize(new Payload(item.Id, user.Id, message)),
            IdempotencyKey = $"{prefix}{item.Version}:{user.Id}", CreatedAt = DateTimeOffset.UtcNow
        });
        }
    }

    public async Task DispatchAsync(CancellationToken ct)
    {
        if (!notifier.IsConfigured) return;
        var candidates = await db.NotificationLogs.AsNoTracking().Where(x => x.Type == NotificationType
            && (x.State == "pending" || x.State == "failed" || x.State == "sending") && x.Attempts < 4).ToListAsync(ct);
        var attempted = 0;
        foreach (var log in candidates.OrderBy(x => x.CreatedAt))
        {
            if (attempted >= 10) break;
            var payload = JsonSerializer.Deserialize<Payload>(log.PayloadJson)!;
            var item = await db.Requirements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == payload.RequirementId, ct);
            if (item is null || !item.GetAssigneeIds().Contains(payload.AssigneeId))
            {
                await db.NotificationLogs.Where(x => x.Id == log.Id && x.State != "sent")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled"), ct);
                continue;
            }
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == payload.AssigneeId, ct);
            // Unbound members wait without consuming retries. Binding enables delivery.
            if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.WeComUserId)) continue;
            var claim = await db.NotificationLogs.Where(x => x.Id == log.Id && x.State == log.State && x.Attempts == log.Attempts)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "sending").SetProperty(x => x.Attempts, x => x.Attempts + 1)
                    .SetProperty(x => x.Recipient, user.WeComUserId), ct);
            if (claim == 0) continue;
            attempted++;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await notifier.SendToUserAsync(user.WeComUserId, payload.Message, timeout.Token);
                await db.NotificationLogs.Where(x => x.Id == log.Id && x.State == "sending")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "sent").SetProperty(x => x.SentAt, DateTimeOffset.UtcNow)
                        .SetProperty(x => x.Error, (string?)null), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Assignment notification {NotificationId} failed", log.Id);
                await db.NotificationLogs.Where(x => x.Id == log.Id && x.State == "sending")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "failed").SetProperty(x => x.Error, ex.Message), ct);
            }
        }
    }
}
