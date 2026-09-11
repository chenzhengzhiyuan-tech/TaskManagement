using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Infrastructure;

public sealed class DistributedJobLock(AppDbContext db)
{
    public async Task<bool> TryAcquireAsync(string key, string owner, TimeSpan duration, CancellationToken ct)
    {
        var now=DateTimeOffset.UtcNow;var expires=now.Add(duration);
        if(db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO ""JobLocks"" (""Key"",""Owner"",""ExpiresAt"") VALUES ({key},{owner},{expires}) ON CONFLICT (""Key"") DO UPDATE SET ""Owner""=EXCLUDED.""Owner"",""ExpiresAt""=EXCLUDED.""ExpiresAt"" WHERE ""JobLocks"".""ExpiresAt"" < {now}",ct);
            return await db.JobLocks.AsNoTracking().AnyAsync(x=>x.Key==key&&x.Owner==owner,ct);
        }
        var entity=await db.JobLocks.FindAsync([key],ct);
        if(entity is not null&&entity.ExpiresAt>now&&entity.Owner!=owner)return false;
        if(entity is null)db.JobLocks.Add(new JobLockEntity{Key=key,Owner=owner,ExpiresAt=expires});else{entity.Owner=owner;entity.ExpiresAt=expires;}
        await db.SaveChangesAsync(ct);return true;
    }
    public async Task ReleaseAsync(string key,string owner,CancellationToken ct)
    {
        var entity=await db.JobLocks.SingleOrDefaultAsync(x=>x.Key==key&&x.Owner==owner,ct);if(entity is null)return;db.JobLocks.Remove(entity);await db.SaveChangesAsync(ct);
    }
}

