using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ground43.Api.Tests;

public sealed class MultipleAssigneeTests
{
    [Fact]
    public async Task DailySummary_IncludesSecondaryAssignee_AndDoesNotDuplicate()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Requirements.ExecuteDeleteAsync(); db.ChangeTracker.Clear();
        foreach (var id in new[] { "u-liu", "u-wang" }) (await db.Users.FindAsync(id))!.WeComUserId = "wx-" + id;
        var item = new RequirementEntity { Id = "REQ-MULTI", Title = "多人临期", Description = "测试", Module = "UI", StatusId = "todo", CreatorId = "u-admin", DueDate = new DateOnly(2026, 9, 12) };
        item.SetAssigneeIds(["u-liu", "u-wang"]); db.Requirements.Add(item); await db.SaveChangesAsync();
        using var worker = new Ground43.Api.Hosted.PlatformMaintenanceService(factory.Services.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Ground43.Api.Hosted.PlatformMaintenanceService>.Instance);
        var now = DateTimeOffset.Parse("2026-09-11T20:04:00+08:00");
        await worker.RunAsync(default, now); await worker.RunAsync(default, now);
        var fake = (FakeWeComNotifier)scope.ServiceProvider.GetRequiredService<IWeComNotifier>();
        Assert.Equal(2, fake.Messages.Count);
        Assert.All(fake.Messages, message => { Assert.Contains("【明日截止】", message.Content); Assert.Contains("REQ-MULTI", message.Content); });
    }

    [Fact]
    public async Task Create_Filter_Reassign_And_Clear_AllAssignees()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { account = "admin", password = "demo123" });
        var auth = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legacy = await client.GetFromJsonAsync<RequirementDto>("/api/requirements/REQ-0048");
        Assert.Equal(new[] { "u-liu" }, legacy!.AssigneeIds);
        var response = await client.PostAsJsonAsync("/api/requirements", new { title = "多人回归", description = "共同处理", module = "UI", priority = "high", statusId = "todo", assigneeIds = new[] { "u-liu", "u-admin", "u-liu" }, reviewerId = "u-chen" });
        response.EnsureSuccessStatusCode(); var item = (await response.Content.ReadFromJsonAsync<RequirementDto>())!;
        Assert.Equal(new[] { "u-liu", "u-admin" }, item.AssigneeIds);
        Assert.Equal("u-chen", item.ReviewerId);
        var tree = await client.GetFromJsonAsync<RequirementTreePageDto>("/api/requirements/tree?mine=true&query=多人回归");
        Assert.Equal(item.Id, Assert.Single(tree!.Groups).Root.Id);
        var filtered = await client.GetFromJsonAsync<RequirementTreePageDto>("/api/requirements/tree?assigneeId=u-admin,u-wang&query=多人回归");
        Assert.Single(filtered!.Groups);
        var prefix = $"assignment:{item.Id}:";
        Assert.Equal(2, await db.NotificationLogs.CountAsync(x => x.IdempotencyKey!.StartsWith(prefix) && x.State == "pending"));
        var patch = await client.PatchAsJsonAsync($"/api/requirements/{item.Id}", new { assigneeIds = new[] { "u-admin", "u-wang" }, version = item.Version });
        patch.EnsureSuccessStatusCode(); var updated = (await patch.Content.ReadFromJsonAsync<RequirementDto>())!;
        db.ChangeTracker.Clear();
        var logs = await db.NotificationLogs.Where(x => x.IdempotencyKey!.StartsWith(prefix)).ToListAsync();
        Assert.Equal("cancelled", Assert.Single(logs.Where(x => x.Recipient == "u-liu")).State);
        Assert.Equal("pending", Assert.Single(logs.Where(x => x.Recipient == "u-admin")).State);
        Assert.Equal("pending", Assert.Single(logs.Where(x => x.Recipient == "u-wang")).State);
        var invalid = await client.PatchAsJsonAsync($"/api/requirements/{item.Id}", new { assigneeIds = new[] { "u-admin", "missing" } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var cleared = await client.PatchAsJsonAsync($"/api/requirements/{item.Id}", new { assigneeIds = Array.Empty<string>(), version = updated.Version });
        cleared.EnsureSuccessStatusCode(); var empty = (await cleared.Content.ReadFromJsonAsync<RequirementDto>())!;
        Assert.Empty(empty.AssigneeIds!); Assert.Null(empty.AssigneeId); Assert.Equal("u-chen", empty.ReviewerId);
    }

    [Fact]
    public async Task Dispatch_NotifiesEveryCurrentAssigneeExactlyOnce()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var id in new[] { "u-liu", "u-wang" }) (await db.Users.FindAsync(id))!.WeComUserId = "wx-" + id;
        var item = (await db.Requirements.FindAsync("REQ-0048"))!;
        item.SetAssigneeIds(["u-liu", "u-wang", "u-liu"]);
        var service = scope.ServiceProvider.GetRequiredService<AssignmentNotificationService>();
        await service.RecordAssigneesChangeAsync(item, [], "u-admin", true, default);
        await db.SaveChangesAsync(); await service.DispatchAsync(default); await service.DispatchAsync(default);
        var fake = (FakeWeComNotifier)scope.ServiceProvider.GetRequiredService<IWeComNotifier>();
        Assert.Equal(2, fake.Messages.Count);
        Assert.Equal(new[] { "wx-u-liu", "wx-u-wang" }, fake.Messages.Select(x => x.UserId).Order().ToArray());
    }

    [Fact]
    public async Task Import_SemicolonAccounts_StoresAllAssignees()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { account = "admin", password = "demo123" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("标题,模块,处理人,验收人\n多人导入,UI,member-a;member-b;member-a,admin", System.Text.Encoding.UTF8), "file", "multi.csv");
        (await client.PostAsync("/api/requirements/import?commit=true", content)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.Requirements.SingleAsync(x => x.Title == "多人导入");
        Assert.Equal(new[] { "u-liu", "u-wang" }, item.GetAssigneeIds()); Assert.Equal("u-admin", item.ReviewerId);
    }
}
