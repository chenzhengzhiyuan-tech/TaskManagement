using System.Security.Claims;
using System.Text.Encodings.Web;
using Ground43.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ground43.Api.Infrastructure;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db,
    SessionTokenService tokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthenticationScheme = "Ground43Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header[7..].Trim();
        if (string.IsNullOrWhiteSpace(token)) return AuthenticateResult.NoResult();
        var hash = tokenService.Hash(token);
        var now = DateTimeOffset.UtcNow;
        var session = await db.AuthSessions.AsNoTracking().Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, Context.RequestAborted);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now || !session.User.IsActive) return AuthenticateResult.Fail("登录会话无效或已过期");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, session.UserId),
            new Claim(ClaimTypes.Name, session.User.Name),
            new Claim(ClaimTypes.Role, session.User.Role),
            new Claim("account", session.User.Account),
            new Claim("session_id", session.Id.ToString())
        };
        var identity = new ClaimsIdentity(claims, AuthenticationScheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), AuthenticationScheme));
    }
}

public static class ClaimsPrincipalExtensions
{
    public static string UserId(this ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Missing user id claim.");
}
