using System.Data;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Infrastructure;

public sealed class IterationRolloverService(AppDbContext db, DistributedJobLock jobLock)
{
    private static readonly TimeZoneInfo China = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "China Standard Time" : "Asia/Shanghai");

    public static (DateOnly Start, DateOnly End) ActiveWeek(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, China);
        var today = DateOnly.FromDateTime(local.DateTime);
        var range = WorkdayService.WeekRange(today);
        // The following week opens at Friday 23:59, including the weekend before its Monday.
        return today > range.End || today == range.End && local.TimeOfDay >= new TimeSpan(23, 59, 0)
            ? WorkdayService.WeekRange(range.Start.AddDays(7)) : range;
    }

    public async Task ReconcileAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // Iteration states, requirement versions and audit records commit together. The lock is
        // held inside the transaction so two service instances cannot partially roll a week over.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        if (!await jobLock.TryAcquireAsync("iteration-rollover", owner, TimeSpan.FromMinutes(2), ct)) return;

        var target = ActiveWeek(now);
        var iterations = await db.Iterations.OrderBy(x => x.StartDate).ToListAsync(ct);
        var byStart = iterations.ToDictionary(x => x.StartDate);
        IterationEntity EnsureWeek(DateOnly start)
        {
            if (byStart.TryGetValue(start, out var existing)) return existing;
            var end = start.AddDays(4);
            var added = new IterationEntity
            {
                Id = $"it-{start:yyyyMMdd}", Name = $"{start:yyyyMMdd}-{end:yyyyMMdd}",
                StartDate = start, EndDate = end, CreatedAt = now,
            };
            db.Iterations.Add(added);
            byStart.Add(start, added);
            return added;
        }

        var earliest = iterations.Select(x => x.StartDate).DefaultIfEmpty(target.Start).Min();
        // Fill missed weeks after downtime and keep exactly one next-week preview.
        for (var start = earliest < target.Start ? earliest : target.Start; start <= target.Start.AddDays(7); start = start.AddDays(7))
            EnsureWeek(start);
        EnsureWeek(target.Start);
        EnsureWeek(target.Start.AddDays(7));

        var ended = byStart.Values.Where(x => x.StartDate < target.Start).OrderBy(x => x.StartDate).ToList();
        var endedIds = ended.Select(x => x.Id).ToArray();
        // Only these two protected statuses stay in the old iteration, not every custom terminal status.
        var pending = await db.Requirements.AsNoTracking().Where(x => endedIds.Contains(x.IterationId!)
            && x.StatusId != "completed" && x.StatusId != "closed").ToListAsync(ct);
        foreach (var iteration in byStart.Values)
            iteration.State = iteration.StartDate < target.Start ? "completed"
                : iteration.StartDate == target.Start ? "active" : "upcoming";
        // Persist target weeks inside this transaction before updating foreign keys.
        await db.SaveChangesAsync(ct);
        foreach (var iteration in ended)
        {
            var next = EnsureWeek(iteration.StartDate.AddDays(7));
            foreach (var requirement in pending.Where(x => x.IterationId == iteration.Id))
            {
                // Column-restricted update: carry the last business state forward, including the
                // original UpdatedAt and deadline. Version is only an internal concurrency token.
                var changed = await db.Requirements.Where(x => x.Id == requirement.Id && x.Version == requirement.Version
                    && x.IterationId == iteration.Id && x.StatusId != "completed" && x.StatusId != "closed")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IterationId, next.Id)
                        .SetProperty(x => x.Version, x => x.Version + 1), ct);
                if (changed != 1) throw new DbUpdateConcurrencyException("需求在迭代顺延期间已被修改，请重试");
                requirement.IterationId = next.Id;
                requirement.Version++;
                db.History.Add(new HistoryEntity
                {
                    Id = Guid.NewGuid(), RequirementId = requirement.Id, ActorId = "u-admin",
                    Action = "迭代顺延", Detail = $"{iteration.Name} → {next.Name}", CreatedAt = now,
                });
            }
        }
        await db.SaveChangesAsync(ct);
        await jobLock.ReleaseAsync("iteration-rollover", owner, ct);
        await transaction.CommitAsync(ct);
    }
}
