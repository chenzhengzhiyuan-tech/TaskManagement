using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Endpoints;

public static class IterationEndpoints
{
    public static IEndpointRouteBuilder MapIterationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/iterations").RequireAuthorization().WithTags("Iterations");
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok((await db.Iterations.AsNoTracking().OrderByDescending(x => x.StartDate).ToListAsync(ct)).Select(Mapping.ToDto)));
        group.MapGet("/{id}/requirements", async (string id, AppDbContext db, CancellationToken ct) => Results.Ok((await db.Requirements.AsNoTracking().Include(x => x.Comments).Include(x => x.History).Include(x => x.Attachments).Where(x => x.IterationId == id).OrderByDescending(x => x.UpdatedAt).ToListAsync(ct)).Select(Mapping.ToDto)));
        return app;
    }
}
