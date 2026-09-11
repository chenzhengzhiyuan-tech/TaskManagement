using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ground43.Api.Data;
using Ground43.Api.Hosted;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ground43.Api.Tests;

public sealed class NotificationTests
{
    [Theory]
    [InlineData(-3, "【已超期 3 天】")]
    [InlineData(0, "【今日截止】")]
    [InlineData(1, "【明日截止】")]
    [InlineData(2, "【2天后截止】")]
    [InlineData(3, "【未完成】")]
    [InlineData(null, "【未完成】")]
    public void DeadlineMessages_UseNaturalCalendarDays(int? offset, string marker)
    {
        var today = new DateOnly(2026, 9, 11); // Friday: Sunday is still two natural days away.
        var item = new RequirementEntity { Id = "REQ-T", Title = "验收通知测试", DueDate = offset.HasValue ? today.AddDays(offset.Value) : null };
        var message = Assert.Single(PlatformMaintenanceService.BuildDailyMessages("admin", today, [item], "http://127.0.0.1:4433/"));
        Assert.Contains(marker, message);
        Assert.Contains("http://127.0.0.1:4433/?requirement=REQ-T", message);
    }

    [Theory]
    [InlineData("2026-09-07T20:04:00+08:00", true)]
    [InlineData("2026-09-07T19:59:00+08:00", false)]
    [InlineData("2026-09-07T20:10:00+08:00", false)]
    [InlineData("2026-09-12T20:04:00+08:00", false)]
    [InlineData("2026-09-25T20:04:00+08:00", false)]
    [InlineData("2026-09-20T20:04:00+08:00", true)]
    public async Task DailySummary_UsesWorkCalendarAndTimeWindow_AndIsIdempotent(string instant, bool expected)
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Requirements.ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        var owner = (await db.Users.FindAsync("u-liu"))!; owner.WeComUserId = "wx-liu";
        var now = DateTimeOffset.Parse(instant); var today = DateOnly.FromDateTime(now.DateTime);
        foreach (var (id, offset) in new[] { ("late", -2), ("today", 0), ("tomorrow", 1), ("two", 2), ("future", 9) })
        {
            var item = Item(id); item.DueDate = today.AddDays(offset); db.Requirements.Add(item);
        }
        db.Requirements.Add(Item("undated"));
        var review = Item("review"); review.StatusId = "review"; db.Requirements.Add(review);
        var finished = Item("finished"); finished.StatusId = "completed"; db.Requirements.Add(finished);
        var unassigned = Item("unassigned"); unassigned.AssigneeId = null; db.Requirements.Add(unassigned);
        await db.SaveChangesAsync();
        using var worker = new PlatformMaintenanceService(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PlatformMaintenanceService>.Instance);
        await worker.RunAsync(default, now);
        await worker.RunAsync(default, now);
        var fake = (FakeWeComNotifier)scope.ServiceProvider.GetRequiredService<IWeComNotifier>();
        Assert.Equal(expected ? 1 : 0, fake.Messages.Count);
        if (expected)
        {
            var message = Assert.Single(fake.Messages); Assert.Equal("wx-liu", message.UserId);
            Assert.Contains("共 7 条", message.Content);
            Assert.Contains("【已超期 2 天】late", message.Content); Assert.Contains("【2天后截止】two", message.Content);
            Assert.DoesNotContain("finished", message.Content); Assert.DoesNotContain("unassigned", message.Content);
            Assert.Equal("sent", (await db.NotificationLogs.SingleAsync()).State);
        }
    }

    [Fact]
    public async Task ReviewTransition_QueuesAtomically_DeduplicatesEdits_AndNotifiesOnResubmission()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await Bind(client, "u-chen", "chen@example.com");
        await Patch(client, "REQ-0048", new { statusId = "review", reviewerId = "u-chen" });
        var fake = factory.Services.GetRequiredService<IWeComNotifier>() as FakeWeComNotifier;
        Assert.Empty(fake!.Messages); // Request saves first; background delivery is separate.
        await Dispatch(factory);
        var first = Assert.Single(fake.Messages);
        Assert.Equal("wx-chen", first.UserId); Assert.Contains("列表加载性能优化", first.Content);
        Assert.Contains("/?requirement=REQ-0048", first.Content); Assert.Contains("已等待验收", first.Content);
        await Patch(client, "REQ-0048", new { statusId = "review", title = "编辑标题" });
        await Bind(client, "u-chen", "chen@example.com");
        await Dispatch(factory); Assert.Single(fake.Messages);
        await Patch(client, "REQ-0048", new { statusId = "in_progress" });
        await Patch(client, "REQ-0048", new { statusId = "review" });
        await Dispatch(factory); Assert.Equal(2, fake.Messages.Count);
    }

    [Fact]
    public async Task ChangingReviewerOrLeavingReview_CancelsObsoleteDeliveries()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await Bind(client, "u-chen", "chen@example.com"); await Bind(client, "u-wang", "wang@example.com");
        await Patch(client, "REQ-0048", new { statusId = "review", reviewerId = "u-chen" });
        await Patch(client, "REQ-0048", new { reviewerId = "u-wang" });
        await Dispatch(factory);
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>();
        Assert.Equal("wx-wang", Assert.Single(fake.Messages).UserId);
        await Patch(client, "REQ-0048", new { reviewerId = "u-chen" });
        await Patch(client, "REQ-0048", new { statusId = "in_progress" });
        await Dispatch(factory); Assert.Single(fake.Messages);
    }

    [Fact]
    public async Task MissingReviewerAndBinding_AreAllowed_AndBindingDeliversPendingNotice()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await Patch(client, "REQ-0048", new { statusId = "review", clearReviewer = true });
        await Dispatch(factory);
        await Patch(client, "REQ-0048", new { reviewerId = "u-chen" });
        await Dispatch(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, (await db.NotificationLogs.SingleAsync(x => x.Type == ReviewNotificationService.NotificationType)).Attempts);
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>(); Assert.Empty(fake.Messages);
        await Bind(client, "u-chen", "chen@example.com");
        await Dispatch(factory); await Dispatch(factory); Assert.Single(fake.Messages);
    }

    [Fact]
    public async Task PreUpgradeReviewTask_NotifiesOnBinding_AndRepeatedBindingDoesNotRepeat()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Requirements.Where(x => x.Id == "REQ-0047").ExecuteUpdateAsync(s => s.SetProperty(x => x.ReviewerId, "u-chen"));
        }
        await Bind(client, "u-chen", "chen@example.com"); await Dispatch(factory);
        await Bind(client, "u-chen", "chen@example.com"); await Dispatch(factory);
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>();
        Assert.Contains("REQ-0047", Assert.Single(fake.Messages).Content);
    }

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(20, 4, false)]
    public async Task FailedDelivery_RetriesWithoutFailingTaskSave_AndStopsAfterThreeRetries(int failures, int attempts, bool succeeds)
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await Bind(client, "u-chen", "chen@example.com");
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>(); fake.FailuresRemaining = failures;
        await Patch(client, "REQ-0048", new { statusId = "review", reviewerId = "u-chen" });
        for (var i = 0; i < 6; i++) await Dispatch(factory);
        Assert.Equal(attempts, fake.SendAttempts); Assert.Equal(succeeds ? 1 : 0, fake.Messages.Count);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.NotificationLogs.SingleAsync(x => x.Type == ReviewNotificationService.NotificationType);
        Assert.Equal(succeeds ? "sent" : "failed", log.State); Assert.Equal(attempts, log.Attempts);
        Assert.Equal("review", (await db.Requirements.FindAsync("REQ-0048"))!.StatusId);
    }

    [Fact]
    public async Task FailedTaskSave_DoesNotEnqueueOrChangeState()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_notification BEFORE INSERT ON NotificationLogs BEGIN SELECT RAISE(ABORT, 'test atomicity'); END;");
        var response = await client.PatchAsJsonAsync("/api/requirements/REQ-0048", new { statusId = "review", reviewerId = "u-chen" });
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal("in_progress", (await db.Requirements.AsNoTracking().SingleAsync(x => x.Id == "REQ-0048")).StatusId);
        Assert.Empty(await db.NotificationLogs.ToListAsync());
    }

    [Fact]
    public async Task CreateAndImportReviewTasks_EnqueueOnlyOnSuccessfulCommit()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        var created = await client.PostAsJsonAsync("/api/requirements", new { title = "新建待验收", description = "测试", statusId = "review", reviewerId = "u-chen" });
        created.EnsureSuccessStatusCode();
        const string csv = "标题,状态,验收人\n导入待验收,待验收,示例成员丙\n";
        foreach (var commit in new[] { false, true })
        {
            using var content = new MultipartFormDataContent(); content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "review.csv");
            (await client.PostAsync($"/api/requirements/import?commit={commit.ToString().ToLowerInvariant()}", content)).EnsureSuccessStatusCode();
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.Equal(commit ? 2 : 1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().NotificationLogs.CountAsync());
        }
    }

    [Fact]
    public async Task DailyRetry_DoesNotResendAlreadyAcceptedMessageParts()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Requirements.ExecuteDeleteAsync(); db.ChangeTracker.Clear();
        (await db.Users.FindAsync("u-liu"))!.WeComUserId = "wx-liu";
        for (var i = 0; i < 12; i++) { var item = Item($"REQ-LONG-{i:D2}"); item.Title = new string('测', 220); db.Requirements.Add(item); }
        await db.SaveChangesAsync();
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>(); fake.FailedAttemptNumbers.Add(2);
        using var worker = new PlatformMaintenanceService(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PlatformMaintenanceService>.Instance);
        var now = DateTimeOffset.Parse("2026-09-07T20:04:00+08:00");
        await worker.RunAsync(default, now); Assert.Single(fake.Messages);
        await worker.RunAsync(default, now.AddMinutes(5));
        var content = string.Join("\n", fake.Messages.Select(x => x.Content));
        for (var i = 0; i < 12; i++) Assert.Equal(1, content.Split($"/?requirement=REQ-LONG-{i:D2}").Length - 1);
        Assert.All(fake.Messages, x => Assert.True(Encoding.UTF8.GetByteCount(x.Content) <= 1900));
        db.ChangeTracker.Clear(); Assert.Equal("sent", (await db.NotificationLogs.SingleAsync()).State);
    }

    [Fact]
    public async Task PendingDelivery_SurvivesWorkerRestart_ButDeletedTasksAreNotSent()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await Bind(client, "u-chen", "chen@example.com");
        await Patch(client, "REQ-0048", new { statusId = "review", reviewerId = "u-chen" });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.NotificationLogs.ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "sending").SetProperty(x => x.Attempts, 1));
        }
        await Dispatch(factory);
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>(); Assert.Single(fake.Messages);
        await Patch(client, "REQ-0048", new { statusId = "todo" });
        await Patch(client, "REQ-0048", new { statusId = "review" });
        (await client.DeleteAsync("/api/requirements/REQ-0048")).EnsureSuccessStatusCode();
        await Dispatch(factory); Assert.Single(fake.Messages);
    }

    [Fact]
    public async Task NewTask_NotifiesAssigneeOnce_AfterSave_WithConfirmedContent()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await Bind(client, "u-admin", "admin@example.com");
        var response = await client.PostAsJsonAsync("/api/requirements", new { title = "新建通知测试", description = "测试", module = "UI", priority = "high", statusId = "todo", assigneeId = "u-admin" });
        response.EnsureSuccessStatusCode();
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>();
        Assert.Empty(fake.Messages);
        await Dispatch(factory); await Dispatch(factory);
        var message = Assert.Single(fake.Messages);
        Assert.Equal("wx-admin", message.UserId);
        Assert.Contains("【新任务】", message.Content); Assert.Contains("新建通知测试", message.Content);
        Assert.Contains("优先级：高", message.Content); Assert.Contains("期望完成：未设置", message.Content);
        Assert.Contains("创建人：", message.Content); Assert.Contains("/?requirement=REQ-", message.Content);
    }

    [Fact]
    public async Task Assignment_OnlyActualChangesNotifyNewOwner_AndCancelsObsolete()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await Bind(client, "u-chen", "chen@example.com"); await Bind(client, "u-wang", "wang@example.com");
        await Patch(client, "REQ-0048", new { assigneeId = "u-chen" });
        await Patch(client, "REQ-0048", new { assigneeId = "u-wang" });
        await Dispatch(factory);
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>();
        var message = Assert.Single(fake.Messages); Assert.Equal("wx-wang", message.UserId);
        Assert.Contains("【任务指派】", message.Content); Assert.Contains("原处理人：示例成员丙", message.Content); Assert.Contains("操作人：", message.Content);
        await Patch(client, "REQ-0048", new { assigneeId = "u-wang", title = "仅编辑标题" });
        await Dispatch(factory); Assert.Single(fake.Messages);
        await Patch(client, "REQ-0048", new { clearAssignee = true });
        await Dispatch(factory); Assert.Single(fake.Messages);
        await Patch(client, "REQ-0048", new { assigneeId = "u-chen" });
        await Dispatch(factory); Assert.Equal(2, fake.Messages.Count);
        Assert.Contains("原处理人：未分配", fake.Messages.Last().Content);
    }

    [Fact]
    public async Task Assignment_WaitsForBinding_AndRetriesWithoutDuplicateSuccess()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await Patch(client, "REQ-0048", new { assigneeId = "u-chen" });
        await Dispatch(factory);
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>(); Assert.Empty(fake.Messages);
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().NotificationLogs.SingleAsync(x => x.Type == "assignment")).Attempts);
        await Bind(client, "u-chen", "chen@example.com"); fake.FailuresRemaining = 1;
        await Dispatch(factory); Assert.Empty(fake.Messages);
        await Dispatch(factory); await Dispatch(factory); Assert.Single(fake.Messages);
    }

    [Fact]
    public async Task Assignment_ImportPreviewDoesNotNotify_CommitNotifiesEachAssignedRow()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await Bind(client, "u-chen", "chen@example.com");
        const string csv = "标题,状态,处理人\n导入通知一,未开始,示例成员丙\n导入通知二,未开始,示例成员丙\n";
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>();
        foreach (var commit in new[] { false, true })
        {
            using var content = new MultipartFormDataContent(); content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "assignment.csv");
            (await client.PostAsync($"/api/requirements/import?commit={commit.ToString().ToLowerInvariant()}", content)).EnsureSuccessStatusCode();
            await Dispatch(factory); Assert.Equal(commit ? 2 : 0, fake.Messages.Count);
        }
        Assert.All(fake.Messages, m => { Assert.Equal("wx-chen", m.UserId); Assert.Contains("【新任务】", m.Content); });
    }

    private static RequirementEntity Item(string id) => new()
    {
        Id = id, Title = id, Description = "test", StatusId = "todo", Module = "UI", CreatorId = "u-admin", AssigneeId = "u-liu",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    private static async Task<HttpClient> Login(ApiFactory factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { account = "admin", password = "demo123" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return client;
    }
    private static async Task Patch(HttpClient client, string id, object body) =>
        (await client.PatchAsJsonAsync($"/api/requirements/{id}", body)).EnsureSuccessStatusCode();
    private static async Task Bind(HttpClient client, string id, string email) =>
        (await client.PostAsJsonAsync($"/api/users/{id}/wecom/bind", new { email })).EnsureSuccessStatusCode();
    private static async Task Dispatch(ApiFactory factory)
    {
        using var worker = new ReviewNotificationWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ReviewNotificationWorker>.Instance);
        await worker.RunAsync(default);
    }
}
