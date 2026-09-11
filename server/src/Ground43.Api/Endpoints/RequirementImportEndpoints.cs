using Ground43.Api.Infrastructure;

namespace Ground43.Api.Endpoints;

public static class RequirementImportEndpoints
{
    public static IEndpointRouteBuilder MapRequirementImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/requirements/import", ImportAsync)
            .RequireAuthorization("Admin")
            .DisableAntiforgery()
            .WithTags("Requirements");
        return app;
    }

    private static async Task<IResult> ImportAsync(IFormFile? file, bool commit, HttpContext context, RequirementImportService importer, CancellationToken ct)
    {
        if (file is null) return Results.BadRequest(new { message = "请选择要导入的 Excel 或 CSV 文件" });
        try
        {
            var result = await importer.RunAsync(file, commit, context.User.UserId(), ct);
            if (commit && result.InvalidRows > 0) return Results.UnprocessableEntity(new { message = "导入数据校验失败，请修正后重试", result });
            return Results.Ok(result);
        }
        catch (RequirementImportException ex) { return Results.BadRequest(new { message = ex.Message }); }
    }
}
