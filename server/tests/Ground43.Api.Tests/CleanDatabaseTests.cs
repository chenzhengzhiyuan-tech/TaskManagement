using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Ground43.PrepareCleanDatabase;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ground43.Api.Tests;

public sealed class CleanDatabaseTests
{
    [Fact]
    public async Task NewCleanDatabase_HasOnlyadminLogin_DefaultsAndCurrentIterations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ground43-clean-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "clean.db");
        const string testPassword = "CleanTest-Only-123!";
        try
        {
            await CleanDatabase.CreateAsync(path, testPassword, DateTimeOffset.Parse("2026-08-31T12:00:00+08:00"));
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()).Options;
            await using var db = new AppDbContext(options);
            var users = await db.Users.ToListAsync();
            var admin = Assert.Single(users, x => x.IsActive);
            Assert.Equal("admin", admin.Name); Assert.Equal("admin", admin.Account);
            Assert.Equal("u-admin", admin.Id); Assert.Equal(Roles.Admin, admin.Role);
            Assert.Null(admin.WeComUserId);
            Assert.True(new PasswordService().Verify(admin, testPassword));
            Assert.False(new PasswordService().Verify(admin, "demo123"));
            Assert.Equal(2, users.Count);
            Assert.False(users.Single(x => x.Id == SystemUsers.DeletedId).IsActive);
            Assert.Empty(await db.Requirements.ToListAsync()); Assert.Empty(await db.History.ToListAsync());
            Assert.Empty(await db.Comments.ToListAsync()); Assert.Empty(await db.Attachments.ToListAsync());
            Assert.Empty(await db.AuthSessions.ToListAsync()); Assert.Empty(await db.NotificationLogs.ToListAsync());
            Assert.Empty(await db.UploadSessions.ToListAsync()); Assert.Empty(await db.CustomFields.ToListAsync());
            Assert.Empty(await db.JobLocks.ToListAsync());
            Assert.Equal(8, await db.Statuses.CountAsync()); Assert.Equal(11, await db.Modules.CountAsync());
            Assert.Equal(3, await db.RequirementTypes.CountAsync());
            Assert.Equal(2, await db.Iterations.CountAsync());
            Assert.Equal("20260831-20260904", (await db.Iterations.SingleAsync(x => x.State == "active")).Name);
            Assert.Equal("G43", (await db.SystemBranding.SingleAsync()).ProjectName);
            Assert.Equal("/login-background.svg", (await db.SystemBranding.SingleAsync()).LoginBackgroundUrl);
            Assert.Null((await db.RequirementDefaults.SingleAsync()).AssigneeId);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RefusesExistingDatabase_WithoutChangingIt()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "must survive");
            await Assert.ThrowsAsync<InvalidOperationException>(() => CleanDatabase.CreateAsync(path, "test-password", DateTimeOffset.UtcNow));
            Assert.Equal("must survive", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExistingSqliteDatabase_AddsWeComEmailAndUniqueUserIdIndexOnStartup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ground43-schema-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "legacy.db");
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()).Options;
            await using (var legacy = new AppDbContext(options))
            {
                await legacy.Database.EnsureCreatedAsync();
                await legacy.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_Users_WeComUserId\"");
                await legacy.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" DROP COLUMN \"WeComEmail\"");
            }

            await using var db = new AppDbContext(options);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Bootstrap:AdminPassword"] = "demo123" }).Build();
            await new DatabaseInitializer(db, new PasswordService(), configuration, new TestHostEnvironment()).InitializeAsync();

            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            var columns = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('Users')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            Assert.Contains("WeComEmail", columns);

            var indexes = new Dictionary<string, bool>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA index_list('Users')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) indexes[reader.GetString(1)] = reader.GetBoolean(2);
            }
            Assert.True(indexes["IX_Users_WeComUserId"]);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Ground43.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
