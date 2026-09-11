using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Infrastructure;

public sealed class WorkdayService(AppDbContext db)
{
    public async Task<bool> IsWorkdayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var configured = await db.WorkCalendar.AsNoTracking().SingleOrDefaultAsync(x => x.Date == date, cancellationToken);
        return configured?.IsWorkday ?? date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    public static (DateOnly Start, DateOnly End) WeekRange(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        var monday = date.AddDays(-offset);
        return (monday, monday.AddDays(4));
    }
}
