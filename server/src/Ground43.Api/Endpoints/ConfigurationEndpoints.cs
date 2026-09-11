using System.Text.Json;
using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Endpoints;

public static class ConfigurationEndpoints
{
    private static readonly string[] FieldTypes = ["text", "number", "date", "single", "multi", "person"];

    public static IEndpointRouteBuilder MapConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var modules = app.MapGroup("/api/modules").RequireAuthorization().WithTags("Modules");
        modules.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok((await db.Modules.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync(ct)).Select(Mapping.ToDto)));
        modules.MapPost("/", AddModuleAsync).RequireAuthorization("Admin");
        modules.MapPatch("/{id:guid}", RenameModuleAsync).RequireAuthorization("Admin");
        modules.MapDelete("/{id:guid}", DeleteModuleAsync).RequireAuthorization("Admin");

        var statuses = app.MapGroup("/api/statuses").RequireAuthorization().WithTags("Statuses");
        statuses.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok((await db.Statuses.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync(ct)).Select(Mapping.ToDto)));
        statuses.MapPost("/", AddStatusAsync).RequireAuthorization("Admin");
        statuses.MapPut("/order", ReorderStatusesAsync).RequireAuthorization("Admin");
        statuses.MapPatch("/{id}", UpdateStatusAsync).RequireAuthorization("Admin");
        statuses.MapDelete("/{id}", DeleteStatusAsync).RequireAuthorization("Admin");

        var fields = app.MapGroup("/api/custom-fields").RequireAuthorization().WithTags("CustomFields");
        fields.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok((await db.CustomFields.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync(ct)).Select(Mapping.ToDto)));
        fields.MapPost("/", AddFieldAsync).RequireAuthorization("Admin");
        fields.MapPatch("/{id}", UpdateFieldAsync).RequireAuthorization("Admin");
        return app;
    }

    private static async Task<IResult> AddModuleAsync(CreateModuleRequest request, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name.Trim(); if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "模块名称不能为空" });
        if (await db.Modules.AnyAsync(x => x.Name == name, ct)) return Results.Conflict(new { message = "模块已存在" });
        var order = (await db.Modules.MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;
        var entity = new ModuleEntity { Id = Guid.NewGuid(), Name = name, SortOrder = order }; db.Modules.Add(entity); await db.SaveChangesAsync(ct);
        return Results.Created($"/api/modules/{entity.Id}", entity.ToDto());
    }

    private static async Task<IResult> RenameModuleAsync(Guid id, CreateModuleRequest request, AppDbContext db, CancellationToken ct)
    {
        var module = await db.Modules.FindAsync([id], ct); if (module is null) return Results.NotFound();
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "模块名称不能为空" });
        if (await db.Modules.AnyAsync(x => x.Id != id && x.Name == name, ct)) return Results.Conflict(new { message = "模块已存在" });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var oldName = module.Name;
        await db.Requirements.Where(x => x.Module == oldName).ExecuteUpdateAsync(s => s.SetProperty(x => x.Module, name).SetProperty(x => x.Version, x => x.Version + 1), ct);
        await db.RequirementDefaults.Where(x => x.Module == oldName).ExecuteUpdateAsync(s => s.SetProperty(x => x.Module, name), ct);
        module.Name = name; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.Ok(module.ToDto());
    }

    private static async Task<IResult> ReorderStatusesAsync(ReorderStatusesRequest request, AppDbContext db, CancellationToken ct)
    {
        var all = await db.Statuses.ToListAsync(ct);
        if (request.Ids is null || request.Ids.Length != all.Count || request.Ids.Distinct().Count() != all.Count || all.Any(x => !request.Ids.Contains(x.Id)))
            return Results.BadRequest(new { message = "状态列表已变化，请刷新后重新排序" });
        for (var index = 0; index < request.Ids.Length; index++) all.Single(x => x.Id == request.Ids[index]).SortOrder = index;
        await db.SaveChangesAsync(ct);
        return Results.Ok(all.OrderBy(x => x.SortOrder).Select(Mapping.ToDto));
    }

    private static async Task<IResult> DeleteModuleAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var module = await db.Modules.FindAsync([id], ct); if (module is null) return Results.NotFound();
        if (await db.Requirements.AnyAsync(x => x.Module == module.Name, ct)) return Results.Conflict(new { message = "仍有需求使用此模块" });
        if (await db.Modules.CountAsync(ct) <= 1) return Results.BadRequest(new { message = "至少保留一个模块" });
        db.Modules.Remove(module); await db.SaveChangesAsync(ct); return Results.NoContent();
    }

    private static async Task<IResult> AddStatusAsync(CreateStatusRequest request, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name.Trim(); if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "状态名称不能为空" });
        var order = (await db.Statuses.MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;
        var entity = new StatusEntity { Id = $"status-{Guid.NewGuid():N}", Name = name, Color = request.Color, SortOrder = order };
        db.Statuses.Add(entity); await db.SaveChangesAsync(ct); return Results.Created($"/api/statuses/{entity.Id}", entity.ToDto());
    }

    private static async Task<IResult> UpdateStatusAsync(string id, UpdateStatusRequest request, AppDbContext db, CancellationToken ct)
    {
        var entity = await db.Statuses.FindAsync([id], ct); if (entity is null) return Results.NotFound();
        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { message = "状态名称不能为空" });
            if (await db.Statuses.AnyAsync(x => x.Id != id && x.Name == request.Name.Trim(), ct)) return Results.Conflict(new { message = "状态名称已存在" });
            entity.Name = request.Name.Trim();
        }
        if (request.Protected.HasValue) entity.Protected = request.Protected.Value;
        if (!string.IsNullOrWhiteSpace(request.Color)) entity.Color = request.Color;
        if (request.SortOrder.HasValue) entity.SortOrder = request.SortOrder.Value;
        await db.SaveChangesAsync(ct); return Results.Ok(entity.ToDto());
    }

    private static async Task<IResult> DeleteStatusAsync(string id, AppDbContext db, CancellationToken ct)
    {
        var entity = await db.Statuses.FindAsync([id], ct); if (entity is null) return Results.NotFound();
        if (await db.Statuses.CountAsync(ct) <= 1) return Results.BadRequest(new { message = "至少保留一个状态" });
        if (await db.RequirementDefaults.AnyAsync(x => x.StatusId == id, ct)) return Results.Conflict(new { message = "需求默认值正在使用此状态，请先修改默认值" });
        if (await db.Requirements.AnyAsync(x => x.StatusId == id, ct)) return Results.Conflict(new { message = "仍有需求使用此状态，请先迁移需求" });
        db.Statuses.Remove(entity); await db.SaveChangesAsync(ct); return Results.NoContent();
    }

    private static async Task<IResult> AddFieldAsync(CreateCustomFieldRequest request, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name.Trim(); if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "字段名称不能为空" });
        if (!FieldTypes.Contains(request.Type)) return Results.BadRequest(new { message = "字段类型无效" });
        if (await db.CustomFields.AnyAsync(x => x.Name == name, ct)) return Results.Conflict(new { message = "字段名称已存在" });
        var order = (await db.CustomFields.MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;
        var entity = new CustomFieldEntity { Id = $"cf-{Guid.NewGuid():N}", Name = name, Type = request.Type, Required = request.Required, Enabled = true, OptionsJson = JsonSerializer.Serialize(request.Options ?? []), SortOrder = order };
        db.CustomFields.Add(entity); await db.SaveChangesAsync(ct); return Results.Created($"/api/custom-fields/{entity.Id}", entity.ToDto());
    }

    private static async Task<IResult> UpdateFieldAsync(string id, UpdateCustomFieldRequest request, AppDbContext db, CancellationToken ct)
    {
        var entity = await db.CustomFields.FindAsync([id], ct); if (entity is null) return Results.NotFound();
        if (!string.IsNullOrWhiteSpace(request.Name)) entity.Name = request.Name.Trim();
        if (request.Required.HasValue) entity.Required = request.Required.Value;
        if (request.Enabled.HasValue) entity.Enabled = request.Enabled.Value;
        if (request.Options is not null) entity.OptionsJson = JsonSerializer.Serialize(request.Options);
        if (request.SortOrder.HasValue) entity.SortOrder = request.SortOrder.Value;
        await db.SaveChangesAsync(ct); return Results.Ok(entity.ToDto());
    }
}
