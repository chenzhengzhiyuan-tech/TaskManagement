using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ground43.Api.Endpoints;

public static class AttachmentEndpoints
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/gif", "image/webp" };

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var uploads = app.MapGroup("/api").RequireAuthorization().WithTags("Attachments");
        uploads.MapPost("/requirements/{requirementId}/attachments/uploads", CreateUploadAsync);
        uploads.MapPut("/attachments/uploads/{uploadId:guid}/chunks/{index:int}", PutChunkAsync).DisableAntiforgery();
        uploads.MapPost("/attachments/uploads/{uploadId:guid}/complete", CompleteAsync);
        uploads.MapDelete("/attachments/uploads/{uploadId:guid}", CancelAsync);
        uploads.MapGet("/attachments/{id:guid}", DownloadAsync);
        uploads.MapDelete("/attachments/{id:guid}", DeleteAsync);
        return app;
    }

    private static async Task<IResult> CreateUploadAsync(string requirementId, CreateUploadRequest request, HttpContext context, AppDbContext db, AttachmentStorage storage, IOptions<StorageOptions> options, CancellationToken ct)
    {
        if (!await db.Requirements.AnyAsync(x => x.Id == requirementId, ct)) return Results.NotFound(new { message = "需求不存在" });
        if (!AllowedTypes.Contains(request.ContentType)) return Results.BadRequest(new { message = "仅支持JPG、PNG、GIF和WebP图片" });
        if (request.TotalSize <= 0 || request.TotalSize > options.Value.MaxAttachmentBytes) return Results.BadRequest(new { message = "附件大小必须在1字节至500MB之间" });
        var chunkSize = Math.Clamp(request.ChunkSize ?? options.Value.ChunkSizeBytes, 1024 * 1024, 16 * 1024 * 1024);
        var now = DateTimeOffset.UtcNow;
        var session = new UploadSessionEntity { Id = Guid.NewGuid(), RequirementId = requirementId, ForComment = request.ForComment, FileName = Path.GetFileName(request.FileName), ContentType = request.ContentType, TotalSize = request.TotalSize, ChunkSize = chunkSize, TotalChunks = (int)Math.Ceiling(request.TotalSize / (double)chunkSize), UploadedById = context.User.UserId(), CreatedAt = now, ExpiresAt = now.AddHours(options.Value.UploadSessionHours) };
        Directory.CreateDirectory(storage.UploadDirectory(session.Id)); db.UploadSessions.Add(session); await db.SaveChangesAsync(ct);
        return Results.Created($"/api/attachments/uploads/{session.Id}", new CreateUploadResponse(session.Id, session.ChunkSize, session.TotalChunks, session.ExpiresAt));
    }

    private static async Task<IResult> PutChunkAsync(Guid uploadId, int index, HttpContext context, AppDbContext db, AttachmentStorage storage, CancellationToken ct)
    {
        var session = await db.UploadSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == uploadId, ct);
        if (session is null || session.CompletedAt is not null) return Results.NotFound();
        if (session.ExpiresAt <= DateTimeOffset.UtcNow) return Results.BadRequest(new { message = "上传会话已过期" });
        if (session.UploadedById != context.User.UserId() && !context.User.IsInRole(Roles.Admin)) return Results.Forbid();
        if (index < 0 || index >= session.TotalChunks) return Results.BadRequest(new { message = "分片序号无效" });
        var max = index == session.TotalChunks - 1 ? session.TotalSize - (long)index * session.ChunkSize : session.ChunkSize;
        if (context.Request.ContentLength is > 0 && context.Request.ContentLength > max) return Results.BadRequest(new { message = "分片大小超过预期" });
        Directory.CreateDirectory(storage.UploadDirectory(uploadId));
        var target = storage.ChunkPath(uploadId, index); var temp = target + ".tmp";
        await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
        {
            var copied = 0L; var buffer = new byte[1024 * 1024]; int read;
            while ((read = await context.Request.Body.ReadAsync(buffer, ct)) > 0)
            {
                copied += read; if (copied > max) { output.Close(); File.Delete(temp); return Results.BadRequest(new { message = "分片大小超过预期" }); }
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        File.Move(temp, target, true); return Results.NoContent();
    }

    private static async Task<IResult> CompleteAsync(Guid uploadId, HttpContext context, AppDbContext db, AttachmentStorage storage, CancellationToken ct)
    {
        var session = await db.UploadSessions.SingleOrDefaultAsync(x => x.Id == uploadId, ct);
        if (session is null || session.CompletedAt is not null) return Results.NotFound();
        if (session.UploadedById != context.User.UserId() && !context.User.IsInRole(Roles.Admin)) return Results.Forbid();
        for (var i = 0; i < session.TotalChunks; i++) if (!File.Exists(storage.ChunkPath(uploadId, i))) return Results.BadRequest(new { message = $"缺少分片 {i}" });
        var extension = Extension(session.ContentType); var attachmentId = Guid.NewGuid();
        string relativePath; string sha;
        try { (relativePath, sha) = await storage.AssembleAsync(uploadId, session.RequirementId, attachmentId, extension, session.TotalChunks, ct); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
        var full = storage.FullPath(relativePath);
        if (new FileInfo(full).Length != session.TotalSize || !HasValidSignature(full, session.ContentType))
        {
            storage.DeleteFile(relativePath); return Results.BadRequest(new { message = "文件大小或图片格式校验失败" });
        }
        var entity = new AttachmentEntity { ForComment = session.ForComment, Id = attachmentId, RequirementId = session.RequirementId, Name = session.FileName, Size = session.TotalSize, ContentType = session.ContentType, RelativePath = relativePath, Sha256 = sha, UploadedById = session.UploadedById, CreatedAt = DateTimeOffset.UtcNow };
        db.Attachments.Add(entity); session.CompletedAt = DateTimeOffset.UtcNow;
        var requirement = await db.Requirements.FindAsync([session.RequirementId], ct);
        if (requirement is not null && !session.ForComment) { requirement.UpdatedAt = DateTimeOffset.UtcNow; requirement.Version++; }
        if (!session.ForComment) db.History.Add(new HistoryEntity { Id = Guid.NewGuid(), RequirementId = session.RequirementId, ActorId = session.UploadedById, Action = "上传附件", Detail = session.FileName, CreatedAt = entity.CreatedAt });
        await db.SaveChangesAsync(ct); storage.DeleteUpload(uploadId);
        return Results.Ok(new CompleteUploadResponse(entity.ToDto()));
    }

    private static async Task<IResult> CancelAsync(Guid uploadId, HttpContext context, AppDbContext db, AttachmentStorage storage, CancellationToken ct)
    {
        var session = await db.UploadSessions.SingleOrDefaultAsync(x => x.Id == uploadId, ct); if (session is null) return Results.NotFound();
        if (session.UploadedById != context.User.UserId() && !context.User.IsInRole(Roles.Admin)) return Results.Forbid();
        db.UploadSessions.Remove(session); await db.SaveChangesAsync(ct); storage.DeleteUpload(uploadId); return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, AppDbContext db, AttachmentStorage storage, CancellationToken ct)
    {
        var attachment = await db.Attachments.FindAsync([id], ct); if (attachment is null) return Results.NotFound();
        if (attachment.UploadedById != context.User.UserId() && !context.User.IsInRole(Roles.Admin)) return Results.Forbid();
        if (attachment.CommentId != null) return Results.BadRequest(new { message = "评论图片不能单独删除" });
        var requirement = await db.Requirements.FindAsync([attachment.RequirementId], ct);
        if (requirement is not null && !attachment.ForComment) { requirement.Version++; requirement.UpdatedAt = DateTimeOffset.UtcNow; }
        db.Attachments.Remove(attachment);
        if (!attachment.ForComment) db.History.Add(new HistoryEntity { Id = Guid.NewGuid(), RequirementId = attachment.RequirementId, ActorId = context.User.UserId(), Action = "删除附件", Detail = attachment.Name, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        storage.DeleteFile(attachment.RelativePath);
        return Results.NoContent();
    }

    private static async Task<IResult> DownloadAsync(Guid id, bool? download, HttpContext context, AppDbContext db, AttachmentStorage storage, CancellationToken ct)
    {
        var attachment = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (attachment is null) return Results.NotFound();
        if (attachment.ForComment && attachment.CommentId == null && attachment.UploadedById != context.User.UserId()) return Results.Forbid();
        var path = storage.FullPath(attachment.RelativePath); if (!File.Exists(path)) return Results.NotFound(new { message = "附件文件不存在" });
        return Results.File(path, attachment.ContentType, download == true ? attachment.Name : null, enableRangeProcessing: true);
    }

    private static string Extension(string type) => type.ToLowerInvariant() switch { "image/jpeg" => ".jpg", "image/png" => ".png", "image/gif" => ".gif", "image/webp" => ".webp", _ => ".bin" };
    private static bool HasValidSignature(string path, string type)
    {
        Span<byte> header = stackalloc byte[12]; using var stream = File.OpenRead(path); var read = stream.Read(header); if (read < 6) return false;
        return type.ToLowerInvariant() switch
        {
            "image/jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/png" => header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/gif" => System.Text.Encoding.ASCII.GetString(header[..6]) is "GIF87a" or "GIF89a",
            "image/webp" => System.Text.Encoding.ASCII.GetString(header[..4]) == "RIFF" && System.Text.Encoding.ASCII.GetString(header[8..12]) == "WEBP",
            _ => false
        };
    }
}
