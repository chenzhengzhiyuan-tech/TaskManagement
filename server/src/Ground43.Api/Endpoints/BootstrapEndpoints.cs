using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Endpoints;

public static class BootstrapEndpoints
{
    public static IEndpointRouteBuilder MapBootstrapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/bootstrap", async (HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var userId = context.User.UserId();
            var currentUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, ct);
            var users = await db.Users.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
            var statuses = await db.Statuses.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync(ct);
            var modules = await db.Modules.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync(ct);
            var iterations = await db.Iterations.AsNoTracking().OrderByDescending(x => x.StartDate).ToListAsync(ct);
            var fields = await db.CustomFields.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.SortOrder).ToListAsync(ct);
            var requirementTypes = await db.RequirementTypes.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.SortOrder).ToListAsync(ct);
            var defaults = await db.RequirementDefaults.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
            var branding = await db.SystemBranding.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
            var requirements = (await db.Requirements.AsNoTracking()
                .Include(x => x.Comments).Include(x => x.History).Include(x => x.Attachments).AsSplitQuery()
                .ToListAsync(ct)).OrderByDescending(x => x.UpdatedAt).ToList();
            return Results.Ok(new BootstrapDto(currentUser.ToDto(), users.Select(Mapping.ToDto).ToArray(), statuses.Select(Mapping.ToDto).ToArray(), iterations.Select(Mapping.ToDto).ToArray(), requirements.Select(Mapping.ToDto).ToArray(), fields.Select(Mapping.ToDto).ToArray(), modules.Select(Mapping.ToDto).ToArray(), requirementTypes.Select(Mapping.ToDto).ToArray(), defaults.ToDto(), branding.ToDto()));
        }).RequireAuthorization().WithTags("Bootstrap");
        return app;
    }
}

