using System.Text.Json;
using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ground43.PrepareCleanDatabase;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--inspect")
        {
            Console.WriteLine(await CleanDatabase.InspectAsync(args[1]));
            return 0;
        }
        if (args.Length != 2 || args[0] != "--output")
            throw new ArgumentException("Usage: Ground43.PrepareCleanDatabase --output NEW_DATABASE_PATH. Password is read from standard input.");
        var password = await Console.In.ReadLineAsync() ?? throw new ArgumentException("Password is required on standard input.");
        await CleanDatabase.CreateAsync(args[1], password, DateTimeOffset.UtcNow);
        Console.WriteLine(JsonSerializer.Serialize(new { created = Path.GetFullPath(args[1]), account = "admin", activeUsers = 1, requirements = 0, verified = true }));
        return 0;
    }
}

public static class CleanDatabase
{
    public static async Task<string> InspectAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync();
        var counts = new Dictionary<string, long>();
        foreach (var table in new[] { "Requirements", "Comments", "History", "Attachments", "Users", "AuthSessions", "Iterations", "CustomFields", "Modules", "Statuses", "RequirementTypes", "NotificationLogs", "UploadSessions" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM [{table}]";
            counts.Add(table, (long)(await command.ExecuteScalarAsync())!);
        }
        var users = new List<object>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id,Account,Name,Role,IsActive FROM Users ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) users.Add(new { id = reader.GetString(0), account = reader.GetString(1), name = reader.GetString(2), role = reader.GetString(3), active = reader.GetInt64(4) == 1 });
        }
        await using var integrityCommand = connection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA integrity_check";
        var integrity = (string)(await integrityCommand.ExecuteScalarAsync())!;
        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check";
        await using var violations = await foreignKeys.ExecuteReaderAsync();
        var foreignKeysValid = !await violations.ReadAsync();
        return JsonSerializer.Serialize(new { counts, users, integrity, foreignKeysValid });
    }

    public static async Task CreateAsync(string newPath, string password, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password must not be empty.");
        var target = Path.GetFullPath(newPath);
        if (!Path.IsPathRooted(newPath) || File.Exists(target))
            throw new InvalidOperationException("Only a NEW absolute database path is accepted. Existing databases are never modified.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Claim this path atomically before SQLite opens it, so a typo/retry cannot overwrite data.
        using (new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(
            new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString()).Options;
        await using var db = new AppDbContext(options);
        var passwords = new PasswordService();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bootstrap:AdminPassword"] = Guid.NewGuid().ToString("N"),
        }).Build();
        var initializer = new DatabaseInitializer(db, passwords, configuration, new EmptyEnvironment());
        await initializer.InitializeAsync();
        db.ChangeTracker.Clear();

        // The source is a newly seeded disposable database, never the live database.
        // Keep standard dictionaries and remove every demo/business record.
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Attachments.ExecuteDeleteAsync();
            await db.Comments.ExecuteDeleteAsync();
            await db.History.ExecuteDeleteAsync();
            await db.Requirements.ExecuteDeleteAsync();
            await db.UploadSessions.ExecuteDeleteAsync();
            await db.NotificationLogs.ExecuteDeleteAsync();
            await db.AuthSessions.ExecuteDeleteAsync();
            await db.JobLocks.ExecuteDeleteAsync();
            await db.CustomFields.ExecuteDeleteAsync();
            await db.Iterations.ExecuteDeleteAsync();
            await db.Users.Where(x => x.Id != "u-admin" && x.Id != SystemUsers.DeletedId).ExecuteDeleteAsync();
            var admin = await db.Users.SingleAsync(x => x.Id == "u-admin");
            admin.Account = "admin"; admin.Name = "admin"; admin.Role = Roles.Admin;
            admin.Initials = "E"; admin.Color = "#0a84ff"; admin.IsActive = true;
            admin.WeComUserId = null; admin.CreatedAt = now; admin.UpdatedAt = now;
            admin.PasswordHash = passwords.Hash(admin, password);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await new IterationRolloverService(db, new DistributedJobLock(db)).ReconcileAsync(now);
        // Simulate the next application startup: no demo members or tasks may reappear.
        await initializer.InitializeAsync();
        var active = await db.Users.AsNoTracking().Where(x => x.IsActive).ToListAsync();
        if (active.Count != 1 || active[0].Name != "admin" || active[0].Account != "admin"
            || active[0].Role != Roles.Admin || !passwords.Verify(active[0], password))
            throw new InvalidOperationException("Administrator verification failed.");
        if (await db.Requirements.AnyAsync() || await db.History.AnyAsync() || await db.Comments.AnyAsync()
            || await db.Attachments.AnyAsync() || await db.AuthSessions.AnyAsync() || await db.CustomFields.AnyAsync())
            throw new InvalidOperationException("Clean database still contains business records.");
        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
        await db.Database.ExecuteSqlRawAsync("VACUUM;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
    }

    private sealed class EmptyEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Ground43.PrepareCleanDatabase";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
