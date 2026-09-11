using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public-settings", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok((await db.SystemBranding.AsNoTracking().SingleAsync(x => x.Id == 1, ct)).ToDto())).AllowAnonymous().WithTags("Public");
        app.MapGet("/api/public-settings/login-background", BackgroundAsync).AllowAnonymous().WithTags("Public");

        var types = app.MapGroup("/api/requirement-types").RequireAuthorization().WithTags("RequirementTypes");
        types.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok((await db.RequirementTypes.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync(ct)).Select(Mapping.ToDto)));
        types.MapPost("/", AddTypeAsync).RequireAuthorization("Admin");
        types.MapPatch("/{id:guid}", UpdateTypeAsync).RequireAuthorization("Admin");

        var defaults = app.MapGroup("/api/requirement-defaults").RequireAuthorization().WithTags("RequirementDefaults");
        defaults.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok((await db.RequirementDefaults.AsNoTracking().SingleAsync(x => x.Id == 1, ct)).ToDto()));
        defaults.MapPatch("/", UpdateDefaultsAsync).RequireAuthorization("Admin");

        var branding = app.MapGroup("/api/system-branding").RequireAuthorization("Admin").WithTags("Branding");
        branding.MapPatch("/", UpdateBrandingAsync);
        branding.MapPost("/background", UploadBackgroundAsync).DisableAntiforgery();

        var calendar = app.MapGroup("/api/work-calendar").RequireAuthorization().WithTags("WorkCalendar");
        calendar.MapGet("/", GetCalendarAsync);
        calendar.MapPut("/{date}", UpdateCalendarAsync).RequireAuthorization("Admin");
        return app;
    }

    private static async Task<IResult> AddTypeAsync(CreateRequirementTypeRequest request, AppDbContext db, CancellationToken ct)
    {
        var name=request.Name.Trim(); if(string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new{message="需求单类型不能为空"});
        if(await db.RequirementTypes.AnyAsync(x=>x.Name==name,ct)) return Results.Conflict(new{message="需求单类型已存在"});
        var order=(await db.RequirementTypes.MaxAsync(x=>(int?)x.SortOrder,ct)??0)+1;
        var used=await db.RequirementTypes.Select(x=>x.Color).ToListAsync(ct);
        var color=NextTypeColor(used,order);
        var entity=new RequirementTypeEntity{Id=Guid.NewGuid(),Name=name,Color=color,SortOrder=order,Enabled=true}; db.RequirementTypes.Add(entity); await db.SaveChangesAsync(ct); return Results.Created($"/api/requirement-types/{entity.Id}",entity.ToDto());
    }
    private static string NextTypeColor(IEnumerable<string> colors, int seed)
    {
        var used = colors.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var palette = new[] { "#0A84FF", "#BF5AF2", "#FF9F0A", "#30D158", "#FF453A", "#64D2FF", "#5E5CE6", "#FF375F", "#AC8E68", "#FFD60A" };
        var available = palette.FirstOrDefault(color => !used.Contains(color));
        if (available is not null) return available;
        for (var offset = 0; offset < 720; offset++)
        {
            var hue = (seed * 137.508 + offset * 53) % 360;
            var candidate = HslToHex(hue, 0.72, 0.52);
            if (!used.Contains(candidate)) return candidate;
        }
        return $"#{Random.Shared.Next(0x1000000):X6}";
    }

    private static string HslToHex(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = lightness - chroma / 2;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        return $"#{(int)Math.Round((red + m) * 255):X2}{(int)Math.Round((green + m) * 255):X2}{(int)Math.Round((blue + m) * 255):X2}";
    }
    private static async Task<IResult> UpdateTypeAsync(Guid id, UpdateRequirementTypeRequest request, AppDbContext db, CancellationToken ct)
    {
        var entity=await db.RequirementTypes.FindAsync([id],ct); if(entity is null)return Results.NotFound();
        if(!string.IsNullOrWhiteSpace(request.Name)) entity.Name=request.Name.Trim(); if(request.SortOrder.HasValue)entity.SortOrder=request.SortOrder.Value; if(request.Enabled.HasValue)entity.Enabled=request.Enabled.Value;
        await db.SaveChangesAsync(ct); return Results.Ok(entity.ToDto());
    }
    private static async Task<IResult> UpdateDefaultsAsync(UpdateRequirementDefaultsRequest request, AppDbContext db, CancellationToken ct)
    {
        var entity=await db.RequirementDefaults.SingleAsync(x=>x.Id==1,ct);
        if(request.Module is not null){if(!await db.Modules.AnyAsync(x=>x.Name==request.Module,ct))return Results.BadRequest(new{message="默认模块不存在"});entity.Module=request.Module;}
        if(request.Priority is not null){if(request.Priority is not ("urgent" or "high" or "medium" or "low"))return Results.BadRequest(new{message="默认优先级无效"});entity.Priority=request.Priority;}
        if(request.StatusId is not null){if(!await db.Statuses.AnyAsync(x=>x.Id==request.StatusId,ct))return Results.BadRequest(new{message="默认状态不存在"});entity.StatusId=request.StatusId;}
        if(request.ClearAssignee)entity.AssigneeId=null;else if(request.AssigneeId is not null)entity.AssigneeId=request.AssigneeId;
        if(request.ClearReviewer)entity.ReviewerId=null;else if(request.ReviewerId is not null)entity.ReviewerId=request.ReviewerId;
        if(request.ClearRequirementType)entity.RequirementTypeId=null;else if(request.RequirementTypeId.HasValue)entity.RequirementTypeId=request.RequirementTypeId;
        if(request.IterationMode is not null){if(request.IterationMode is not ("current" or "none" or "specific"))return Results.BadRequest(new{message="迭代默认模式无效"});entity.IterationMode=request.IterationMode;}
        if(request.IterationId is not null)entity.IterationId=request.IterationId;
        if(request.ClearDueDateOffset)entity.DueDateOffsetDays=null;else if(request.DueDateOffsetDays.HasValue)entity.DueDateOffsetDays=Math.Clamp(request.DueDateOffsetDays.Value,0,365);
        if(request.DescriptionTemplate is not null)entity.DescriptionTemplate=request.DescriptionTemplate;
        await db.SaveChangesAsync(ct); return Results.Ok(entity.ToDto());
    }
    private static async Task<IResult> UpdateBrandingAsync(UpdateBrandingRequest request, AppDbContext db, CancellationToken ct)
    {
        var entity=await db.SystemBranding.SingleAsync(x=>x.Id==1,ct); if(!string.IsNullOrWhiteSpace(request.ProjectName))entity.ProjectName=request.ProjectName.Trim(); entity.UpdatedAt=DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); return Results.Ok(entity.ToDto());
    }
    private static async Task<IResult> UploadBackgroundAsync(IFormFile file, AppDbContext db, AttachmentStorage storage, CancellationToken ct)
    {
        if(file.Length<=0||file.Length>20*1024*1024)return Results.BadRequest(new{message="背景图大小必须在20MB以内"});
        var allowed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"image/jpeg",".jpg"},{"image/png",".png"},{"image/webp",".webp"}}; if(!allowed.TryGetValue(file.ContentType,out var ext))return Results.BadRequest(new{message="仅支持JPG、PNG和WebP"});
        var dir=Path.Combine(storage.RootPath,"branding");Directory.CreateDirectory(dir);var path=Path.Combine(dir,"login-background"+ext);await using(var output=File.Create(path)){await file.CopyToAsync(output,ct);}
        foreach(var old in Directory.GetFiles(dir,"login-background.*").Where(x=>!x.Equals(path,StringComparison.OrdinalIgnoreCase)))File.Delete(old);
        var entity=await db.SystemBranding.SingleAsync(x=>x.Id==1,ct);entity.LoginBackgroundUrl="/api/public-settings/login-background?v="+DateTimeOffset.UtcNow.ToUnixTimeSeconds();entity.UpdatedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);return Results.Ok(entity.ToDto());
    }
    private static async Task<IResult> BackgroundAsync(AppDbContext db, AttachmentStorage storage, IWebHostEnvironment env, CancellationToken ct)
    {
        var entity=await db.SystemBranding.AsNoTracking().SingleAsync(x=>x.Id==1,ct);var dir=Path.Combine(storage.RootPath,"branding");var custom=Directory.Exists(dir)?Directory.GetFiles(dir,"login-background.*").FirstOrDefault():null;
        if(custom is not null){var type=Path.GetExtension(custom).ToLowerInvariant() switch{".png"=>"image/png",".webp"=>"image/webp",_=>"image/jpeg"};return Results.File(custom,type,enableRangeProcessing:true);}
        var bundled=Path.Combine(env.WebRootPath??Path.Combine(env.ContentRootPath,"wwwroot"),"login-background.svg");return File.Exists(bundled)?Results.File(bundled,"image/svg+xml",enableRangeProcessing:true):Results.NotFound();
    }
    private static async Task<IResult> GetCalendarAsync(int? year, AppDbContext db, CancellationToken ct)
    {
        var value=year??DateTime.Now.Year;var start=new DateOnly(value,1,1);var end=new DateOnly(value,12,31);return Results.Ok((await db.WorkCalendar.AsNoTracking().Where(x=>x.Date>=start&&x.Date<=end).OrderBy(x=>x.Date).ToListAsync(ct)).Select(Mapping.ToDto));
    }
    private static async Task<IResult> UpdateCalendarAsync(string date, UpdateWorkCalendarRequest request, AppDbContext db, CancellationToken ct)
    {
        if(!DateOnly.TryParse(date,out var parsed))return Results.BadRequest(new{message="日期格式无效"});var entity=await db.WorkCalendar.FindAsync([parsed],ct);if(entity is null){entity=new WorkCalendarEntity{Date=parsed};db.WorkCalendar.Add(entity);}entity.IsWorkday=request.IsWorkday;entity.Note=request.Note?.Trim();await db.SaveChangesAsync(ct);return Results.Ok(entity.ToDto());
    }
}
