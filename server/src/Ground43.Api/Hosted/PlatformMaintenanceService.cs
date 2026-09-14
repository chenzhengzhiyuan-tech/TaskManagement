using System.Text.Json;
using System.Text;
using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ground43.Api.Hosted;

public sealed class PlatformMaintenanceService(IServiceScopeFactory scopes, ILogger<PlatformMaintenanceService> logger) : BackgroundService
{
    private sealed record DailyDelivery(string[] Requirements, string[]? Messages, int SentParts = 0);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try { await RunAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Platform maintenance cycle failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunAsync(CancellationToken ct, DateTimeOffset? instant = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<AttachmentStorage>();
        var workdays = scope.ServiceProvider.GetRequiredService<WorkdayService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IWeComNotifier>();
        var jobLock = scope.ServiceProvider.GetRequiredService<DistributedJobLock>();
        var storageOptions = scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value;
        var platformOptions = scope.ServiceProvider.GetRequiredService<IOptions<PlatformOptions>>().Value;
        var now = instant ?? DateTimeOffset.UtcNow;
        var lockOwner=$"{Environment.MachineName}:{Environment.ProcessId}";
        if(!await jobLock.TryAcquireAsync("platform-maintenance",lockOwner,TimeSpan.FromMinutes(4),ct))return;
        try
        {
        var china = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "China Standard Time" : "Asia/Shanghai");
        var local = TimeZoneInfo.ConvertTime(now, china);
        var today = DateOnly.FromDateTime(local.DateTime);

        await CleanupExpiredSessionsAsync(db, storage, now, ct);
        await db.SaveChangesAsync(ct);
        if (local.Hour == 20 && local.Minute < 10 && await workdays.IsWorkdayAsync(today, ct)) await SendDailyRemindersAsync(db, notifier, platformOptions.PublicBaseUrl, today, ct);
        if (local.Minute < 10) await CheckCapacityAsync(db, notifier, storage, storageOptions, local, ct);
        await db.SaveChangesAsync(ct);
        }
        finally
        {
            // Releasing the lock must not accidentally persist a failed notification batch.
            db.ChangeTracker.Clear();
            await jobLock.ReleaseAsync("platform-maintenance",lockOwner,ct);
        }
    }

    internal static async Task CleanupExpiredSessionsAsync(AppDbContext db, AttachmentStorage storage, DateTimeOffset now, CancellationToken ct)
    {
        // SQLite cannot translate DateTimeOffset inequalities. Select only timestamps/IDs first,
        // then compare in .NET so SQLite and PostgreSQL use identical expiration semantics.
        var sessions = await db.AuthSessions.AsNoTracking().Select(x => new { x.Id, x.ExpiresAt, x.RevokedAt }).ToListAsync(ct);
        var sessionIds = sessions.Where(x => x.ExpiresAt <= now || x.RevokedAt < now.AddDays(-7)).Select(x => x.Id).ToArray();
        await db.AuthSessions.Where(x => sessionIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var uploads = await db.UploadSessions.AsNoTracking().Where(x => x.CompletedAt == null).Select(x => new { x.Id, x.ExpiresAt }).ToListAsync(ct);
        var uploadIds = uploads.Where(x => x.ExpiresAt <= now).Select(x => x.Id).ToArray();
        foreach (var id in uploadIds) storage.DeleteUpload(id);
        await db.UploadSessions.Where(x => uploadIds.Contains(x.Id) && x.CompletedAt == null).ExecuteDeleteAsync(ct);
        // Only unpublished comment images expire; historical requirement/comment images are retained.
        var drafts = await db.Attachments.AsNoTracking().Where(x => x.ForComment && x.CommentId == null).ToListAsync(ct);
        foreach (var draft in drafts.Where(x => x.CreatedAt < now.AddDays(-1)))
        {
            var deleted = await db.Attachments.Where(x => x.Id == draft.Id && x.ForComment && x.CommentId == null).ExecuteDeleteAsync(ct);
            if (deleted == 1) storage.DeleteFile(draft.RelativePath);
        }
    }

    private static async Task SendDailyRemindersAsync(AppDbContext db, IWeComNotifier notifier, string publicBaseUrl, DateOnly today, CancellationToken ct)
    {
        if (!notifier.IsConfigured) return;
        var keyPrefix = $"daily-reminder:{today:yyyyMMdd}:";
        var terminal = await db.Statuses.Where(x => x.Terminal).Select(x => x.Id).ToListAsync(ct);
        var users = await db.Users.Where(x => x.IsActive && x.WeComUserId != null).ToListAsync(ct);
        foreach (var user in users)
        {
            var key = keyPrefix + user.Id;
            var log = await db.NotificationLogs.SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
            if (log?.State == "sent" || log?.Attempts >= 3) continue;
            var items = (await db.Requirements.AsNoTracking().AssignedTo(user.Id).Where(x => !terminal.Contains(x.StatusId)).ToListAsync(ct))
                .OrderBy(x => x.DueDate is null ? 5 : x.DueDate < today ? 0 : x.DueDate == today ? 1
                    : x.DueDate == today.AddDays(1) ? 2 : x.DueDate == today.AddDays(2) ? 3 : 4)
                .ThenBy(x => x.DueDate)
                .ThenBy(x => x.Id)
                .ToList();
            var delivery = log is null ? null : JsonSerializer.Deserialize<DailyDelivery>(log.PayloadJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items.Count == 0 && delivery?.SentParts is not > 0) continue;
            if (delivery?.Messages is null || delivery.SentParts == 0)
                delivery = new DailyDelivery(items.Select(x => x.Id).ToArray(), BuildDailyMessages(user.Name, today, items, publicBaseUrl).ToArray());
            if (log is null)
            {
                log = new NotificationLogEntity { Id = Guid.NewGuid(), Type = "daily-reminder", Recipient = user.WeComUserId!, Subject = "每日未完成事项汇总", IdempotencyKey = key, CreatedAt = DateTimeOffset.UtcNow };
                db.NotificationLogs.Add(log);
            }
            log.PayloadJson = JsonSerializer.Serialize(delivery);
            log.Attempts++;
            try
            {
                while (delivery.SentParts < delivery.Messages!.Length)
                {
                    await notifier.SendToUserAsync(user.WeComUserId!, delivery.Messages[delivery.SentParts], ct);
                    delivery = delivery with { SentParts = delivery.SentParts + 1 };
                    log.PayloadJson = JsonSerializer.Serialize(delivery);
                    // Checkpoint accepted parts so a later part's failure does not resend them.
                    await db.SaveChangesAsync(ct);
                }
                log.State = "sent"; log.SentAt = DateTimeOffset.UtcNow; log.Error = null;
            }
            catch (Exception ex)
            {
                log.State = "failed"; log.Error = ex.Message;
                try { await notifier.SendAdminAlertAsync($"企业微信需求提醒发送失败：{user.Name}，{ex.Message}", ct); }
                catch { }
            }
        }
    }

    internal static IReadOnlyList<string> BuildDailyMessages(string memberName, DateOnly today, IReadOnlyList<RequirementEntity> items, string publicBaseUrl)
    {
        const int maxBytes = 1900;
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? "http://127.0.0.1:4433" : publicBaseUrl.TrimEnd('/');
        var header = $"{memberName}，{today:yyyy-MM-dd} 未完成事项（共 {items.Count} 条）";
        var messages = new List<string>(); var current = header;
        foreach (var item in items)
        {
            var remainingDays = item.DueDate?.DayNumber - today.DayNumber;
            var marker = remainingDays switch
            {
                < 0 => $"【已超期 {-remainingDays} 天】",
                0 => "【今日截止】",
                1 => "【明日截止】",
                2 => "【2天后截止】",
                _ => "【未完成】"
            };
            var title = item.Title.Length > 220 ? item.Title[..220] + "…" : item.Title;
            var due = item.DueDate?.ToString("yyyy-MM-dd") ?? "未设置";
            var link = $"{baseUrl}/?requirement={Uri.EscapeDataString(item.Id)}";
            var entry = $"\n\n{marker}{item.Id} {title}\n截止：{due}\n{link}";
            if (Encoding.UTF8.GetByteCount(current + entry) > maxBytes && current != header)
            {
                messages.Add(current); current = header + entry;
            }
            else current += entry;
        }
        messages.Add(current);
        return messages;
    }

    private static async Task CheckCapacityAsync(AppDbContext db, IWeComNotifier notifier, AttachmentStorage storage, StorageOptions options, DateTimeOffset local, CancellationToken ct)
    {
        var root = Path.GetPathRoot(storage.RootPath); if (string.IsNullOrWhiteSpace(root)) return;
        var drive = new DriveInfo(root); var used = 100 - (int)Math.Round(drive.AvailableFreeSpace * 100d / drive.TotalSize);
        if (used < options.CapacityWarningPercent) return;
        var key = $"capacity:{local:yyyyMMddHH}:{used / 5 * 5}";
        if (await db.NotificationLogs.AnyAsync(x => x.IdempotencyKey == key, ct)) return;
        var state = used >= options.CapacityCriticalPercent ? "critical" : "warning";
        db.NotificationLogs.Add(new NotificationLogEntity { Id = Guid.NewGuid(), Type = "capacity", Recipient = "admin", Subject = "存储容量告警", PayloadJson = JsonSerializer.Serialize(new { usedPercent = used, path = storage.RootPath }), IdempotencyKey = key, State = "sent", Attempts = 1, CreatedAt = DateTimeOffset.UtcNow, SentAt = DateTimeOffset.UtcNow });
        await notifier.SendAdminAlertAsync($"Ground43存储容量{(state == "critical" ? "严重" : "预警")}：已使用 {used}%（{storage.RootPath}）", ct);
    }
}
