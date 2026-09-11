using System.Text.Json;
using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Read-only live snapshot; every mutation below targets a brand new disposable database copy.
if (args.Length is < 2 or > 3 || args.Length == 3 && args[2] != "--prepare-ui") throw new ArgumentException("Expected source database and NEW destination database paths, optionally --prepare-ui.");
var sourcePath = Path.GetFullPath(args[0]);
var destinationPath = Path.GetFullPath(args[1]);
if (sourcePath == destinationPath || File.Exists(destinationPath)) throw new InvalidOperationException("Destination must not exist or be the source.");
Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly }.ToString()))
await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath }.ToString()))
{
    await source.OpenAsync();
    await destination.OpenAsync();
    source.BackupDatabase(destination);
}
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = destinationPath }.ToString()).Options;
await using var db = new AppDbContext(options);
var now = DateTimeOffset.UtcNow;
var target = IterationRolloverService.ActiveWeek(now);
var beforeIterations = await db.Iterations.AsNoTracking().ToListAsync();
var before = await db.Requirements.AsNoTracking().ToListAsync();
var historiesBefore = await db.History.CountAsync();
var beforeComments = await db.Comments.CountAsync();
var beforeAttachments = await db.Attachments.CountAsync();
var service = new IterationRolloverService(db, new DistributedJobLock(db));
await service.ReconcileAsync(now);
var active = await db.Iterations.SingleAsync(x => x.State == "active");
var after = await db.Requirements.AsNoTracking().ToListAsync();
if (before.Count != after.Count) throw new Exception("Requirement count changed.");
var moved = new List<string>();
foreach (var old in before)
{
    var updated = after.Single(x => x.Id == old.Id);
    var iteration = beforeIterations.SingleOrDefault(x => x.Id == old.IterationId);
    var shouldMove = iteration is not null && iteration.StartDate < target.Start && old.StatusId is not ("completed" or "closed");
    if (updated.IterationId != (shouldMove ? active.Id : old.IterationId)) throw new Exception($"Unexpected iteration for {old.Id}");
    if (shouldMove) moved.Add(old.Id);
    // Normalize only iteration and internal concurrency version; timestamps must also be preserved.
    updated.IterationId = old.IterationId; updated.Version = old.Version;
    if (JsonSerializer.Serialize(old) != JsonSerializer.Serialize(updated)) throw new Exception($"Unexpected field change for {old.Id}");
}
var historyCount = await db.History.CountAsync();
await service.ReconcileAsync(now);
if (historyCount != await db.History.CountAsync()) throw new Exception("Repeat run duplicated history.");
if (beforeComments != await db.Comments.CountAsync() || beforeAttachments != await db.Attachments.CountAsync()) throw new Exception("Comments or attachments changed.");
if (args.Length == 3)
{
    // Disposable loopback UI testing only. Never carry production sessions into a test server.
    await db.AuthSessions.ExecuteDeleteAsync();
    var admin = await db.Users.SingleAsync(x => x.Id == "u-admin");
    admin.PasswordHash = new PasswordService().Hash(admin, "demo123");
    await db.SaveChangesAsync();
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    sourceOpenedReadOnly = true, copyDatabase = destinationPath, active = active.Name,
    requirementsBefore = before.Count, requirementsAfter = after.Count, movedCount = moved.Count, moved,
    poolUnchanged = before.Count(x => x.IterationId == null), newHistoryRecords = historyCount - historiesBefore,
    otherFieldsUnchanged = true, commentsAndAttachmentsUnchanged = true, repeatRunIsIdempotent = true,
    iterations = await db.Iterations.AsNoTracking().OrderByDescending(x => x.StartDate).Select(x => new { x.Name, x.State, x.StartDate, x.EndDate }).ToListAsync(),
}, new JsonSerializerOptions { WriteIndented = true }));
