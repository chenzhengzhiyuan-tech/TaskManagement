using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/reports/summary", async (AppDbContext db, CancellationToken ct) =>
        {
            var requirements = await db.Requirements.AsNoTracking().Include(x => x.Status).ToListAsync(ct);
            var users = await db.Users.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
            var total = requirements.Count; var completed = requirements.Count(x => x.Status.Terminal); var open = total - completed;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var overdue = requirements.Count(x => !x.Status.Terminal && x.DueDate.HasValue && x.DueDate.Value < today);
            var loads = users.Select(user => new MemberLoadDto(user.Id, user.Name, requirements.Count(x => x.GetAssigneeIds().Contains(user.Id) && !x.Status.Terminal))).ToArray();
            return Results.Ok(new ReportSummaryDto(total, completed, open, overdue, total == 0 ? 0 : (int)Math.Round(completed * 100d / total), loads));
        }).RequireAuthorization().WithTags("Reports");
        return app;
    }
}
