using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Ground43.Api.Hosted;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Ground43.Api.Tests;

public sealed class CommentCollaborationTests
{
    [Fact]
    public async Task Mentions_SaveMultiline_DeduplicateRetries_AndRecoverNotificationFailures()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = (await db.Users.FindAsync("u-liu"))!; member.WeComUserId = "wx-test"; await db.SaveChangesAsync();
        var tag = "@" + member.Name;
        var request = new CreateCommentRequest($"{tag} 请检查\n{tag} 第二行", Guid.NewGuid(), [new(member.Id, member.Name, 0, tag.Length), new(member.Id, member.Name, tag.Length + 5, tag.Length)]);
        var response = await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", request);
        response.EnsureSuccessStatusCode();
        var comment = (await response.Content.ReadFromJsonAsync<CommentDto>())!;
        Assert.Contains("\n", comment.Content); Assert.Equal(2, comment.Mentions.Count);
        (await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", request)).EnsureSuccessStatusCode();
        Assert.Equal(1, await db.Comments.CountAsync(x => x.Id == request.RequestId));
        Assert.Single(await db.NotificationLogs.Where(x => x.Type == "comment-mention").ToListAsync());
        var fake = (FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>();
        fake.FailuresRemaining = 1;
        await Dispatch(factory); Assert.Empty(fake.Messages);
        await Dispatch(factory); await Dispatch(factory);
        Assert.Contains($"&comment={comment.Id}", Assert.Single(fake.Messages).Content);
        Assert.Equal("wx-test", fake.Messages[0].UserId);
        var log = await db.NotificationLogs.AsNoTracking().SingleAsync(x => x.Type == "comment-mention");
        Assert.Equal("sent", log.State); Assert.Equal(2, log.Attempts);
    }

    [Fact]
    public async Task UnboundInactiveAndForgedMentionsAreRejected_PlainAtTextDoesNotNotify()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = (await db.Users.FindAsync("u-liu"))!;
        var tag = "@" + member.Name;
        async Task Reject(CreateCommentRequest request) => Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", request)).StatusCode);
        await Reject(new(tag, Guid.NewGuid(), [new(member.Id, member.Name, 0, tag.Length)]));
        member.WeComUserId = "wx-test"; member.IsActive = false; await db.SaveChangesAsync();
        await Reject(new(tag, Guid.NewGuid(), [new(member.Id, member.Name, 0, tag.Length)]));
        member.IsActive = true; await db.SaveChangesAsync();
        await Reject(new("ordinary text", Guid.NewGuid(), [new(member.Id, member.Name, 0, tag.Length)]));
        (await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", new CreateCommentRequest(tag))).EnsureSuccessStatusCode();
        Assert.Empty(await db.NotificationLogs.Where(x => x.Type == "comment-mention").ToListAsync());
    }

    [Fact]
    public async Task ImageOnlyComments_AreSeparateFromRequirementAttachments_AndCannotBeReused()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        var image = await Upload(client, "REQ-0048", true);
        var before = await client.GetFromJsonAsync<RequirementDto>("/api/requirements/REQ-0048");
        Assert.DoesNotContain(before!.Attachments, x => x.Id == image.Id);
        var request = new CreateCommentRequest("", Guid.NewGuid(), AttachmentIds: [Guid.Parse(image.Id)]);
        var response = await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", request); response.EnsureSuccessStatusCode();
        var comment = (await response.Content.ReadFromJsonAsync<CommentDto>())!; Assert.Single(comment.Attachments);
        var after = (await client.GetFromJsonAsync<RequirementDto>("/api/requirements/REQ-0048"))!;
        Assert.Single(after.Comments.Single(x => x.Id == comment.Id).Attachments);
        Assert.DoesNotContain(after.Attachments, x => x.Id == image.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", request with { RequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/attachments/{image.Id}")).StatusCode);
        (await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", request)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CrossTaskOrOrdinaryAttachmentsCannotBeClaimed_AndFailedSaveRollsBackClaim()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        var other = await Upload(client, "REQ-0047", true);
        var ordinary = await Upload(client, "REQ-0048", false);
        foreach (var image in new[] { other, ordinary })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", new CreateCommentRequest("test", Guid.NewGuid(), AttachmentIds: [Guid.Parse(image.Id)]))).StatusCode);
        var own = await Upload(client, "REQ-0048", true);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var beforeVersion = await db.Requirements.Where(x => x.Id == "REQ-0048").Select(x => x.Version).SingleAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_new_comment BEFORE INSERT ON Comments BEGIN SELECT RAISE(ABORT, 'test rollback'); END;");
        var request = new CreateCommentRequest("must roll back", Guid.NewGuid(), AttachmentIds: [Guid.Parse(own.Id)]);
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", request)).StatusCode);
        Assert.Null((await db.Attachments.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(own.Id))).CommentId);
        Assert.Equal(beforeVersion, await db.Requirements.Where(x => x.Id == "REQ-0048").Select(x => x.Version).SingleAsync());
        Assert.False(await db.Comments.AnyAsync(x => x.Id == request.RequestId));
    }

    [Fact]
    public async Task UnbindingBeforeDeliveryCancelsCommentNotice_WithoutLaterBackfill()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = (await db.Users.FindAsync("u-liu"))!; member.WeComUserId = "wx-before"; await db.SaveChangesAsync();
        var tag = "@" + member.Name;
        (await client.PostAsJsonAsync("/api/requirements/REQ-0048/comments", new CreateCommentRequest(tag, Guid.NewGuid(), [new(member.Id, member.Name, 0, tag.Length)]))).EnsureSuccessStatusCode();
        member.WeComUserId = null; await db.SaveChangesAsync(); await Dispatch(factory);
        member.WeComUserId = "wx-after"; await db.SaveChangesAsync(); await Dispatch(factory);
        Assert.Empty(((FakeWeComNotifier)factory.Services.GetRequiredService<IWeComNotifier>()).Messages);
        Assert.Equal("cancelled", (await db.NotificationLogs.AsNoTracking().SingleAsync(x => x.Type == "comment-mention")).State);
    }

    [Fact]
    public async Task OldSqliteCommentsAndAttachmentsSurviveAdditiveUpgradeAndRepeatStartup()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        var image = await Upload(client, "REQ-0048", false);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var comments = await db.Comments.AsNoTracking().Select(x => new { x.Id, x.Content }).ToListAsync();
        foreach (var sql in new[] { "ALTER TABLE Comments DROP COLUMN MentionsJson", "ALTER TABLE Attachments DROP COLUMN CommentId", "ALTER TABLE Attachments DROP COLUMN ForComment", "ALTER TABLE UploadSessions DROP COLUMN ForComment" })
            await db.Database.ExecuteSqlRawAsync(sql);
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        Assert.Equal(comments.OrderBy(x => x.Id), (await db.Comments.AsNoTracking().Select(x => new { x.Id, x.Content }).ToListAsync()).OrderBy(x => x.Id));
        var kept = await db.Attachments.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(image.Id));
        Assert.False(kept.ForComment); Assert.Null(kept.CommentId);
    }

    [Fact]
    public void SqliteSnapshotMatchesTheNewModel()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void PostgresCommentMigrationGeneratesNativeColumnTypes()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var script = db.GetService<IMigrator>().GenerateScript("20260911093000_MultipleAssignees", "20260914120000_CommentCollaboration");
        Assert.Contains("\"CommentId\" uuid", script);
        Assert.Contains("\"ForComment\" boolean", script);
        Assert.Contains("\"MentionsJson\" text", script);
        Assert.DoesNotContain("DROP TABLE", script);
    }

    [Fact]
    public async Task StatusSortingOrdersBeforePaging_AndPreservesChildren()
    {
        using var factory = new ApiFactory(); using var client = await Login(factory);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Requirements.ExecuteDeleteAsync();
        foreach (var (id, status, priority) in new[] { ("REQ-9999", "todo", "high"), ("REQ-10000", "todo", "high"), ("REQ-0003", "todo", "urgent"), ("REQ-0004", "paused", "urgent"), ("REQ-0005", "completed", "urgent") })
            db.Requirements.Add(new RequirementEntity { Id = id, Title = id, StatusId = status, Priority = priority, CreatorId = "u-admin", Module = "UI", Description = "test" });
        db.Requirements.Add(new RequirementEntity { Id = "REQ-0006", ParentId = "REQ-0003", Title = "child", StatusId = "closed", Priority = "low", CreatorId = "u-admin", Module = "UI", Description = "test" });
        await db.SaveChangesAsync();
        var first = (await client.GetFromJsonAsync<RequirementTreePageDto>("/api/requirements/tree?sort=status&pageSize=2"))!;
        Assert.Equal(new[] { "REQ-0003", "REQ-9999" }, first.Groups.Select(x => x.Root.Id));
        Assert.Equal("REQ-0006", Assert.Single(first.Groups[0].Children).Id);
        var second = (await client.GetFromJsonAsync<RequirementTreePageDto>("/api/requirements/tree?sort=status&pageSize=2&page=2"))!;
        Assert.Equal(new[] { "REQ-10000", "REQ-0004" }, second.Groups.Select(x => x.Root.Id));
    }

    private static async Task<HttpClient> Login(ApiFactory factory)
    {
        var client = factory.CreateClient(); var response = await client.PostAsJsonAsync("/api/auth/login", new { account = "admin", password = "demo123" }); response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!; client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token); return client;
    }
    private static async Task Dispatch(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<CommentNotificationService>().DispatchAsync(default);
    }
    private static async Task<AttachmentDto> Upload(HttpClient client, string requirement, bool forComment)
    {
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
        var response = await client.PostAsJsonAsync($"/api/requirements/{requirement}/attachments/uploads", new { fileName = "test.png", contentType = "image/png", totalSize = bytes.Length, forComment }); response.EnsureSuccessStatusCode();
        var upload = (await response.Content.ReadFromJsonAsync<CreateUploadResponse>())!;
        (await client.PutAsync($"/api/attachments/uploads/{upload.UploadId}/chunks/0", new ByteArrayContent(bytes))).EnsureSuccessStatusCode();
        var complete = await client.PostAsync($"/api/attachments/uploads/{upload.UploadId}/complete", null); complete.EnsureSuccessStatusCode();
        return (await complete.Content.ReadFromJsonAsync<CompleteUploadResponse>())!.Attachment;
    }
}
