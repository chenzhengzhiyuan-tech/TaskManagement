using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ground43.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");
        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapGet("/me", async (HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == context.User.UserId(), ct);
            return Results.Ok(user.ToDto());
        }).RequireAuthorization();
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, AppDbContext db, PasswordService passwordService, SessionTokenService tokens, IOptions<AuthOptions> options, CancellationToken ct)
    {
        var member = request.Account.Trim().ToLowerInvariant();
        var user = await db.Users
            .Where(x => x.IsActive && (x.Name.ToLower() == member || x.Account.ToLower() == member))
            .OrderBy(x => x.Name.ToLower() == member ? 0 : 1)
            .FirstOrDefaultAsync(ct);
        if (user is null || !passwordService.Verify(user, request.Password)) return Results.Problem("成员或密码错误", statusCode: StatusCodes.Status401Unauthorized);
        var token = tokens.CreateToken();
        var now = DateTimeOffset.UtcNow;
        var session = new AuthSessionEntity { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = tokens.Hash(token), CreatedAt = now, LastSeenAt = now, ExpiresAt = now.AddDays(options.Value.SessionDays) };
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new LoginResponse(token, session.ExpiresAt, user.ToDto()));
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, AppDbContext db, CancellationToken ct)
    {
        var value = context.User.FindFirst("session_id")?.Value;
        if (Guid.TryParse(value, out var id))
        {
            var session = await db.AuthSessions.FindAsync([id], ct);
            if (session is not null) session.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return Results.NoContent();
    }
}
