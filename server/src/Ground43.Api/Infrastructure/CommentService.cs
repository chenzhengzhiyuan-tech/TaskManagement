using System.Data;
using System.Text.Json;
using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Infrastructure;

public sealed class CommentService(AppDbContext db, CommentNotificationService notifications)
{
    public async Task<IResult> CreateAsync(string requirementId, CreateCommentRequest request, string userId, CancellationToken ct)
    {
        // Keep the original text: mention offsets use UTF-16, as in the browser.
        var content = request.Content ?? "";
        var mentions = (request.Mentions ?? []).OrderBy(x => x.Start).ToArray();
        var attachmentIds = (request.AttachmentIds ?? []).Distinct().Order().ToArray();
        if (string.IsNullOrWhiteSpace(content) && attachmentIds.Length == 0) return Invalid("请输入评论或添加附件");
        if (content.Length > 100_000 || mentions.Length > 100 || attachmentIds.Length > 100) return Invalid("评论内容过长，请分条发送");
        var commentId = request.RequestId ?? Guid.NewGuid();
        if (commentId == Guid.Empty) return Invalid("评论提交标识无效");
        var mentionsJson = JsonSerializer.Serialize(mentions);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var previous = await db.Comments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == commentId, ct);
        if (previous is not null)
        {
            var previousImages = await db.Attachments.AsNoTracking().Where(x => x.CommentId == commentId).ToListAsync(ct);
            if (previous.RequirementId != requirementId || previous.AuthorId != userId || previous.Content != content
                || previous.MentionsJson != mentionsJson || !attachmentIds.SequenceEqual(previousImages.Select(x => x.Id).Order()))
                return Results.Conflict(new { message = "该提交标识已用于另一条评论，请刷新确认上次结果" });
            return Results.Ok(previous.ToDto(previousImages));
        }
        var requirement = await db.Requirements.SingleOrDefaultAsync(x => x.Id == requirementId, ct);
        if (requirement is null) return Results.NotFound();
        var mentionedIds = mentions.Select(x => x.UserId).Distinct().ToArray();
        var recipients = await db.Users.Where(x => mentionedIds.Contains(x.Id)).ToListAsync(ct);
        if (recipients.Count != mentionedIds.Length || recipients.Any(x => !x.IsActive || string.IsNullOrWhiteSpace(x.WeComUserId)))
            return Invalid("只能 @ 已启用且已绑定企微的成员，请移除失效的 @ 成员后重试");
        var end = 0;
        foreach (var mention in mentions)
        {
            var user = recipients.Single(x => x.Id == mention.UserId);
            if (mention.Start < end || mention.Length <= 0 || mention.Start < 0 || mention.Start > content.Length - mention.Length
                || mention.Name != user.Name || content.Substring(mention.Start, mention.Length) != "@" + user.Name)
                return Invalid("@ 成员内容已变化，请重新选择成员");
            end = mention.Start + mention.Length;
        }
        var images = await db.Attachments.Where(x => attachmentIds.Contains(x.Id)).ToListAsync(ct);
        if (images.Count != attachmentIds.Length || images.Any(x => x.RequirementId != requirementId || x.UploadedById != userId || !x.ForComment || x.CommentId != null))
            return Invalid("评论附件已失效或不属于当前评论，请重新上传");
        var now = DateTimeOffset.UtcNow;
        var comment = new CommentEntity { Id = commentId, RequirementId = requirementId, AuthorId = userId, Content = content, MentionsJson = mentionsJson, CreatedAt = now };
        db.Comments.Add(comment);
        // Conditional claims prevent two submissions from attaching the same draft image.
        foreach (var image in images)
        {
            var changed = await db.Attachments.Where(x => x.Id == image.Id && x.CommentId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CommentId, commentId), ct);
            if (changed != 1) return Results.Conflict(new { message = "附件已被另一条评论使用，请刷新" });
            image.CommentId = commentId;
        }
        var summary = content.Trim();
        db.History.Add(new HistoryEntity { Id = Guid.NewGuid(), RequirementId = requirementId, ActorId = userId,
            Action = "添加评论", Detail = summary.Length == 0 ? $"添加了 {images.Count} 个附件" : summary.Length > 60 ? summary[..60] + "…" : summary, CreatedAt = now });
        requirement.UpdatedAt = now; requirement.Version++;
        var author = await db.Users.SingleAsync(x => x.Id == userId, ct);
        notifications.Enqueue(comment, requirement, author, recipients);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/requirements/{requirementId}/comments/{commentId}", comment.ToDto(images));
    }
    private static IResult Invalid(string message) => Results.BadRequest(new { message });
}
