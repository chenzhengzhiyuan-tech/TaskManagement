using Ground43.Api.Data;
using Ground43.Api.Hosted;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ground43.Api.Tests;

public sealed class IterationTests
{
    [Theory]
    [InlineData("2026-08-28T23:58:59+08:00", "2026-08-24")]
    [InlineData("2026-08-28T23:59:00+08:00", "2026-08-31")]
    [InlineData("2026-08-28T15:59:00Z", "2026-08-31")]
    [InlineData("2026-08-29T00:00:00+08:00", "2026-08-31")]
    [InlineData("2026-08-30T23:59:59+08:00", "2026-08-31")]
    [InlineData("2026-08-31T00:00:00+08:00", "2026-08-31")]
    [InlineData("2026-09-04T23:59:00+08:00", "2026-09-07")]
    [InlineData("2027-12-31T23:59:00+08:00", "2028-01-03")]
    public void ActiveWeek_UsesChinaFridayBoundary(string instant, string expectedStart)
    {
        var range = IterationRolloverService.ActiveWeek(DateTimeOffset.Parse(instant));
        Assert.Equal(DateOnly.Parse(expectedStart), range.Start);
        Assert.Equal(range.Start.AddDays(4), range.End);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FridayRollover_MovesOnlyEligibleItems_AndIsIdempotent(bool deleteNextWeek)
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Requirements.ExecuteDeleteAsync();
        if (deleteNextWeek) await db.Iterations.Where(x => x.Id == "it-next").ExecuteDeleteAsync();
        foreach (var status in new[] { "todo", "in_progress", "review", "paused", "online", "completed", "closed" })
            db.Requirements.Add(Item(status, status, "it-current"));
        db.Requirements.Add(Item("pool", "todo", null));
        await db.SaveChangesAsync();
        var rollover = scope.ServiceProvider.GetRequiredService<IterationRolloverService>();
        await rollover.ReconcileAsync(DateTimeOffset.Parse("2026-08-28T23:58:59+08:00"));
        Assert.Equal("active", (await db.Iterations.FindAsync("it-current"))!.State);
        Assert.Empty(await db.History.ToListAsync());

        var boundary = DateTimeOffset.Parse("2026-08-28T23:59:00+08:00");
        var finalUpdatedAt = await db.Requirements.ToDictionaryAsync(x => x.Id, x => x.UpdatedAt);
        await rollover.ReconcileAsync(boundary);
        db.ChangeTracker.Clear();
        var active = await db.Iterations.SingleAsync(x => x.State == "active");
        Assert.Equal(new DateOnly(2026, 8, 31), active.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 4), active.EndDate);
        Assert.Equal("completed", (await db.Iterations.FindAsync("it-current"))!.State);
        Assert.Single(await db.Iterations.Where(x => x.State == "upcoming").ToListAsync());
        foreach (var item in await db.Requirements.ToListAsync())
        {
            if (item.Id == "pool") { Assert.Null(item.IterationId); Assert.Equal(1, item.Version); }
            else if (item.StatusId is "completed" or "closed") { Assert.Equal("it-current", item.IterationId); Assert.Equal(1, item.Version); }
            else { Assert.Equal(active.Id, item.IterationId); Assert.Equal(2, item.Version); }
            Assert.Equal(finalUpdatedAt[item.Id], item.UpdatedAt);
        }
        var history = await db.History.ToListAsync();
        Assert.Equal(5, history.Count);
        Assert.All(history, x => Assert.Equal("20260824-20260828 → 20260831-20260904", x.Detail));
        var count = await db.Iterations.CountAsync();
        await rollover.ReconcileAsync(DateTimeOffset.Parse("2026-08-31T10:00:00+08:00"));
        Assert.Equal(count, await db.Iterations.CountAsync());
        Assert.Equal(5, await db.History.CountAsync());
        Assert.Empty(await db.JobLocks.ToListAsync());
    }

    [Fact]
    public async Task Restart_CatchesUpMultipleMissedWeeks_AndRepairsStaleStates()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Requirements.ExecuteDeleteAsync();
        // A previously completed iteration may still contain stranded unfinished requirements.
        (await db.Iterations.FindAsync("it-current"))!.State = "completed";
        (await db.Iterations.FindAsync("it-next"))!.State = "active";
        db.Requirements.Add(Item("stranded", "todo", "it-current"));
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IterationRolloverService>()
            .ReconcileAsync(DateTimeOffset.Parse("2026-09-21T10:00:00+08:00"));
        db.ChangeTracker.Clear();
        var active = await db.Iterations.SingleAsync(x => x.State == "active");
        Assert.Equal(new DateOnly(2026, 9, 21), active.StartDate);
        Assert.Equal(active.Id, (await db.Requirements.FindAsync("stranded"))!.IterationId);
        Assert.Equal(4, await db.History.CountAsync());
        Assert.Equal(5, (await db.Requirements.FindAsync("stranded"))!.Version);
        Assert.All(await db.Iterations.Where(x => x.StartDate < active.StartDate).ToListAsync(), x => Assert.Equal("completed", x.State));
    }

    [Fact]
    public async Task EmptyIterationTable_CreatesCurrentAndNext_WithoutDuplicateTracking()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Requirements.ExecuteDeleteAsync();
        await db.Iterations.ExecuteDeleteAsync();
        await scope.ServiceProvider.GetRequiredService<IterationRolloverService>()
            .ReconcileAsync(DateTimeOffset.Parse("2026-08-31T10:00:00+08:00"));
        Assert.Equal(2, await db.Iterations.CountAsync());
        Assert.Equal("20260831-20260904", (await db.Iterations.SingleAsync(x => x.State == "active")).Name);
    }

    [Fact]
    public async Task SqliteCleanup_HandlesExpiredAndRevokedSessions_WithoutDateTranslationFailure()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.Parse("2026-08-31T10:00:00+08:00");
        var liveId = Guid.NewGuid(); var recentlyRevokedId = Guid.NewGuid();
        db.AuthSessions.AddRange(
            Session(liveId, now.AddDays(1)),
            Session(Guid.NewGuid(), now),
            Session(Guid.NewGuid(), now.AddDays(1), now.AddDays(-8)),
            Session(recentlyRevokedId, now.AddDays(1), now.AddDays(-1)));
        var liveUpload = Guid.NewGuid(); var expiredUpload = Guid.NewGuid(); var completedUpload = Guid.NewGuid();
        db.UploadSessions.AddRange(Upload(liveUpload, now.AddDays(1)), Upload(expiredUpload, now), Upload(completedUpload, now, now.AddHours(-1)));
        await db.SaveChangesAsync();
        await PlatformMaintenanceService.CleanupExpiredSessionsAsync(db, scope.ServiceProvider.GetRequiredService<AttachmentStorage>(), now, default);
        Assert.Equal(2, await db.AuthSessions.CountAsync());
        Assert.True(await db.AuthSessions.AnyAsync(x => x.Id == liveId));
        Assert.True(await db.AuthSessions.AnyAsync(x => x.Id == recentlyRevokedId));
        Assert.False(await db.UploadSessions.AnyAsync(x => x.Id == expiredUpload));
        Assert.Equal(2, await db.UploadSessions.CountAsync());
        var maintenance = new PlatformMaintenanceService(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PlatformMaintenanceService>.Instance);
        await maintenance.RunAsync(default);
    }

    [Fact]
    public async Task IterationWorker_ReconcilesBeforeStartReturns_WithoutWeCom()
    {
        using var factory = new ApiFactory(weComConfigured: false);
        using var worker = new IterationMaintenanceService(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<IterationMaintenanceService>.Instance);
        await worker.StartAsync(default);
        await worker.StopAsync(default);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active = await db.Iterations.SingleAsync(x => x.State == "active");
        Assert.Equal(IterationRolloverService.ActiveWeek(DateTimeOffset.UtcNow).Start, active.StartDate);
    }

    [Fact]
    public async Task FailedHistoryWrite_RollsBackStatesRequirementsAndNewWeeks()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Requirements.ExecuteDeleteAsync();
        db.Requirements.Add(Item("atomic", "todo", "it-current"));
        await db.SaveChangesAsync();
        var count = await db.Iterations.CountAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_rollover BEFORE INSERT ON History BEGIN SELECT RAISE(ABORT, 'test rollback'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<IterationRolloverService>()
            .ReconcileAsync(DateTimeOffset.Parse("2026-08-31T10:00:00+08:00")));
        db.ChangeTracker.Clear();
        Assert.Equal(count, await db.Iterations.CountAsync());
        Assert.Equal("active", (await db.Iterations.FindAsync("it-current"))!.State);
        Assert.Equal("it-current", (await db.Requirements.FindAsync("atomic"))!.IterationId);
        Assert.Equal(1, (await db.Requirements.FindAsync("atomic"))!.Version);
        Assert.Empty(await db.History.ToListAsync());
        Assert.Empty(await db.JobLocks.ToListAsync());
    }

    [Fact]
    public async Task Rollover_PreservesEveryBusinessColumnAndRelations_WithoutReviewNotifications()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Requirements.ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        var states = new[] { "backlog", "todo", "in_progress", "review", "paused", "online", "completed", "closed" };
        foreach (var state in states)
        {
            var item = Item(state, state, "it-current");
            item.Title = "切换前最终标题 " + state; item.Description = "切换前最终描述";
            item.AssigneeId = "u-liu"; item.ReviewerId = "u-chen"; item.Priority = "high";
            item.DueDate = new DateOnly(2026, 8, 27); item.CustomValuesJson = "{\"risk\":\"high\"}";
            item.RequirementTypeId = Guid.Parse("11111111-1111-1111-1111-111111111101");
            if (state != "backlog") item.ParentId = "backlog";
            item.Comments.Add(new CommentEntity { Id = Guid.NewGuid(), RequirementId = item.Id, AuthorId = "u-admin", Content = "保留评论", CreatedAt = item.CreatedAt });
            item.Attachments.Add(new AttachmentEntity { Id = Guid.NewGuid(), RequirementId = item.Id, Name = "image.png", Size = 10, ContentType = "image/png", RelativePath = "file.png", Sha256 = "hash", UploadedById = "u-admin", CreatedAt = item.CreatedAt });
            db.Requirements.Add(item);
        }
        await db.SaveChangesAsync();
        var before = db.ChangeTracker.Entries<RequirementEntity>().ToDictionary(x => x.Entity.Id, x => x.CurrentValues.Clone());
        var comments = await db.Comments.Select(x => x.Id).ToListAsync();
        var attachments = await db.Attachments.Select(x => x.Id).ToListAsync();
        var service = scope.ServiceProvider.GetRequiredService<IterationRolloverService>();
        await service.ReconcileAsync(DateTimeOffset.Parse("2026-09-07T10:00:00+08:00"));
        db.ChangeTracker.Clear();
        foreach (var item in await db.Requirements.ToListAsync())
        {
            foreach (var property in before[item.Id].Properties.Where(x => x.Name is not "IterationId" and not "Version"))
                Assert.Equal(before[item.Id][property.Name], db.Entry(item).CurrentValues[property.Name]);
            Assert.Equal(item.StatusId is "completed" or "closed" ? "it-current" : "it-20260907", item.IterationId);
        }
        Assert.Equal(comments.Order(), (await db.Comments.Select(x => x.Id).ToListAsync()).Order());
        Assert.Equal(attachments.Order(), (await db.Attachments.Select(x => x.Id).ToListAsync()).Order());
        var auditCount = await db.History.CountAsync();
        await service.ReconcileAsync(DateTimeOffset.Parse("2026-09-07T10:05:00+08:00"));
        Assert.Equal(auditCount, await db.History.CountAsync());
        Assert.Empty(await db.NotificationLogs.ToListAsync());
    }

    private static RequirementEntity Item(string id, string status, string? iteration) => new()
    {
        Id = id, Title = id, Module = "UI", StatusId = status, CreatorId = "u-admin", IterationId = iteration,
        Description = "iteration regression", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static AuthSessionEntity Session(Guid id, DateTimeOffset expires, DateTimeOffset? revoked = null) => new()
    {
        Id = id, UserId = "u-admin", TokenHash = id.ToString(), ExpiresAt = expires, RevokedAt = revoked,
    };

    private static UploadSessionEntity Upload(Guid id, DateTimeOffset expires, DateTimeOffset? completed = null) => new()
    {
        Id = id, RequirementId = "test", FileName = "test.png", ContentType = "image/png", UploadedById = "u-admin",
        ExpiresAt = expires, CompletedAt = completed,
    };
}
