using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace Ground43.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization().WithTags("Users");
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok((await db.Users.AsNoTracking().Where(x => x.Id != SystemUsers.DeletedId).OrderBy(x => x.Name).ToListAsync(ct)).Select(Mapping.ToDto)));
        group.MapPost("/wecom/validate", ValidateWeComAsync).RequireAuthorization("Admin");
        group.MapPost("/{id}/wecom/bind", BindWeComAsync).RequireAuthorization("Admin");
        group.MapDelete("/{id}/wecom/bind", UnbindWeComAsync).RequireAuthorization("Admin");
        group.MapPost("/", CreateAsync).RequireAuthorization("Admin");
        group.MapPatch("/{id}", UpdateAsync).RequireAuthorization("Admin");
        group.MapDelete("/{id}", DeleteAsync).RequireAuthorization("Admin");
        return app;
    }

    private static async Task<IResult> ValidateWeComAsync(ValidateWeComUserRequest request, IWeComNotifier weCom, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null) return Results.BadRequest(new { message = "请输入有效的企业邮箱" });
        if (!weCom.IsConfigured) return Results.Problem("企业微信自建应用尚未配置，请先通过服务端环境变量配置 CorpId、AgentId 和 Secret。", statusCode: StatusCodes.Status503ServiceUnavailable);
        try
        {
            var profile = await weCom.LookupUserByEmailAsync(email, ct);
            return Results.Ok(new WeComUserProfileDto(profile.UserId, profile.Name, profile.Departments, profile.Status, profile.Status == 1));
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> CreateAsync(CreateUserRequest request, AppDbContext db, PasswordService passwords, IWeComNotifier weCom, CancellationToken ct)
    {
        var account = request.Account.Trim();
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(request.Password)) return Results.BadRequest(new { message = "成员、账号和密码不能为空" });
        if (request.Role is not (Roles.Admin or Roles.Developer)) return Results.BadRequest(new { message = "角色无效" });
        var accountKey = account.ToLowerInvariant();
        var nameKey = name.ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Account.ToLower() == accountKey || x.Name.ToLower() == accountKey, ct)) return Results.Conflict(new { message = "账号已存在或与成员名称冲突" });
        if (await db.Users.AnyAsync(x => x.Name.ToLower() == nameKey || x.Account.ToLower() == nameKey, ct)) return Results.Conflict(new { message = "成员名称已存在或与账号冲突" });
        string? weComEmail = null;
        string? weComUserId = null;
        if (!string.IsNullOrWhiteSpace(request.WeComEmail))
        {
            weComEmail = NormalizeEmail(request.WeComEmail);
            if (weComEmail is null) return Results.BadRequest(new { message = "请输入有效的企业邮箱" });
            if (!weCom.IsConfigured)
                return Results.Problem("企业微信自建应用尚未配置，无法在创建时绑定企微；可不验证先新增成员。", statusCode: StatusCodes.Status503ServiceUnavailable);
            WeComUserProfile profile;
            try { profile = await weCom.LookupUserByEmailAsync(weComEmail, ct); }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway); }
            if (profile.Status != 1) return Results.BadRequest(new { message = $"企业微信账号“{profile.UserId}”当前不是已激活成员" });
            if (await db.Users.AnyAsync(x => x.WeComUserId == profile.UserId, ct))
                return Results.Conflict(new { message = "该企业微信成员已绑定到其他平台账号" });
            weComUserId = profile.UserId;
        }
        var now = DateTimeOffset.UtcNow;
        var user = new UserEntity { Id = $"u-{Guid.NewGuid():N}", Account = account, Name = name, Role = request.Role, Initials = string.IsNullOrWhiteSpace(request.Initials) ? name[..1] : request.Initials.Trim(), Color = request.Color ?? "#0a84ff", WeComEmail = weComEmail, WeComUserId = weComUserId, IsActive = true, CreatedAt = now, UpdatedAt = now, PasswordHash = string.Empty };
        user.PasswordHash = passwords.Hash(user, request.Password);
        db.Users.Add(user); await db.SaveChangesAsync(ct); return Results.Created($"/api/users/{user.Id}", user.ToDto());
    }

    private static async Task<IResult> BindWeComAsync(string id, BindWeComUserRequest request, AppDbContext db, IWeComNotifier weCom, ReviewNotificationService notifications, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct); if (user is null || !user.IsActive) return Results.NotFound();
        var email = NormalizeEmail(request.Email);
        if (email is null) return Results.BadRequest(new { message = "请输入有效的企业邮箱" });
        if (!weCom.IsConfigured) return Results.Problem("企业微信自建应用尚未配置。", statusCode: StatusCodes.Status503ServiceUnavailable);
        WeComUserProfile profile;
        try { profile = await weCom.LookupUserByEmailAsync(email, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway); }
        if (profile.Status != 1) return Results.BadRequest(new { message = $"企业微信账号“{profile.UserId}”当前不是已激活成员" });
        if (await db.Users.AnyAsync(x => x.Id != id && x.WeComUserId == profile.UserId, ct))
            return Results.Conflict(new { message = "该企业微信成员已绑定到其他平台账号" });
        user.WeComEmail = email; user.WeComUserId = profile.UserId; user.UpdatedAt = DateTimeOffset.UtcNow;
        await notifications.OnUserBoundAsync(id, ct);
        await db.SaveChangesAsync(ct); return Results.Ok(user.ToDto());
    }

    private static async Task<IResult> UnbindWeComAsync(string id, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct); if (user is null || !user.IsActive) return Results.NotFound();
        user.WeComEmail = null; user.WeComUserId = null; user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); return Results.Ok(user.ToDto());
    }

    private static async Task<IResult> UpdateAsync(string id, UpdateUserRequest request, AppDbContext db, PasswordService passwords, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct); if (user is null) return Results.NotFound();
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim(); var key = name.ToLowerInvariant();
            if (await db.Users.AnyAsync(x => x.Id != id && (x.Name.ToLower() == key || x.Account.ToLower() == key), ct)) return Results.Conflict(new { message = "成员名称已存在或与账号冲突" });
            user.Name = name; user.Initials = user.Name[..1];
        }
        if (!string.IsNullOrWhiteSpace(request.Role) && request.Role is Roles.Admin or Roles.Developer && user.Id != "u-admin") user.Role = request.Role;
        if (!string.IsNullOrWhiteSpace(request.Color)) user.Color = request.Color;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = passwords.Hash(user, request.Password);
            var sessions = await db.AuthSessions.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct);
            foreach (var session in sessions) session.RevokedAt = DateTimeOffset.UtcNow;
        }
        user.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); return Results.Ok(user.ToDto());
    }

    private static async Task<IResult> DeleteAsync(string id, AppDbContext db, CancellationToken ct)
    {
        if (id is "u-admin" or SystemUsers.DeletedId) return Results.BadRequest(new { message = "系统保留账号不能删除" });
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return Results.NotFound();
        if (!await db.Users.AnyAsync(x => x.Id == SystemUsers.DeletedId, ct))
            return Results.Problem("历史归属账号缺失，请重启服务完成数据库升级后重试。", statusCode: StatusCodes.Status503ServiceUnavailable);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var requirement in await db.Requirements.AssignedTo(id).ToListAsync(ct))
            requirement.SetAssigneeIds(requirement.GetAssigneeIds().Where(value => value != id));
        await db.SaveChangesAsync(ct);
        await db.Requirements.Where(x => x.ReviewerId == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReviewerId, (string?)null), ct);
        await db.Requirements.Where(x => x.CreatorId == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CreatorId, SystemUsers.DeletedId), ct);
        await db.Comments.Where(x => x.AuthorId == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.AuthorId, SystemUsers.DeletedId), ct);
        await db.History.Where(x => x.ActorId == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ActorId, SystemUsers.DeletedId), ct);
        await db.Attachments.Where(x => x.UploadedById == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UploadedById, SystemUsers.DeletedId), ct);
        await db.UploadSessions.Where(x => x.UploadedById == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UploadedById, SystemUsers.DeletedId), ct);
        await db.RequirementDefaults.Where(x => x.AssigneeId == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.AssigneeId, (string?)null), ct);
        await db.RequirementDefaults.Where(x => x.ReviewerId == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReviewerId, (string?)null), ct);
        await db.AuthSessions.Where(x => x.UserId == id).ExecuteDeleteAsync(ct);

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var email = value.Trim().ToLowerInvariant();
        return MailAddress.TryCreate(email, out var parsed) && parsed.Address.Equals(email, StringComparison.OrdinalIgnoreCase)
            ? email
            : null;
    }
}
