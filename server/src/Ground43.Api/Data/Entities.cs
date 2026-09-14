namespace Ground43.Api.Data;

public static class Roles
{
    public const string Admin = "admin";
    public const string Developer = "developer";
}

public static class SystemUsers
{
    public const string DeletedId = "u-deleted";
}

public sealed class UserEntity
{
    public string Id { get; set; } = default!;
    public string Account { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Role { get; set; } = Roles.Developer;
    public string Initials { get; set; } = default!;
    public string Color { get; set; } = "#0a84ff";
    public string PasswordHash { get; set; } = default!;
    public string? WeComEmail { get; set; }
    public string? WeComUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AuthSessionEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public string TokenHash { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public UserEntity User { get; set; } = default!;
}

public sealed class StatusEntity
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Color { get; set; } = "#8e8e93";
    public int SortOrder { get; set; }
    public bool Terminal { get; set; }
    public bool Protected { get; set; }
}

public sealed class ModuleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public int SortOrder { get; set; }
}

public sealed class RequirementTypeEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Color { get; set; } = "#0A84FF";
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class RequirementDefaultsEntity
{
    public int Id { get; set; } = 1;
    public string? Module { get; set; }
    public string Priority { get; set; } = "medium";
    public string? StatusId { get; set; }
    public string? AssigneeId { get; set; }
    public string? ReviewerId { get; set; }
    public Guid? RequirementTypeId { get; set; }
    public string IterationMode { get; set; } = "current";
    public string? IterationId { get; set; }
    public int? DueDateOffsetDays { get; set; }
    public string DescriptionTemplate { get; set; } = string.Empty;
}

public sealed class SystemBrandingEntity
{
    public int Id { get; set; } = 1;
    public string ProjectName { get; set; } = "G43";
    public string LoginBackgroundUrl { get; set; } = "/login-background.svg";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class JobLockEntity
{
    public string Key { get; set; } = default!;
    public string Owner { get; set; } = default!;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class IterationEntity
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string State { get; set; } = "upcoming";
    public string Goal { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RequirementEntity
{
    public string? AssigneeIdsJson { get; set; }
    public string[] GetAssigneeIds() => AssigneeIdsJson is null
        ? (AssigneeId is null ? [] : [AssigneeId])
        : System.Text.Json.JsonSerializer.Deserialize<string[]>(AssigneeIdsJson) ?? [];
    public void SetAssigneeIds(IEnumerable<string> ids)
    {
        var values = ids.Distinct(StringComparer.Ordinal).ToArray();
        AssigneeIdsJson = System.Text.Json.JsonSerializer.Serialize(values);
        AssigneeId = values.FirstOrDefault();
    }
    public string Id { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Module { get; set; } = default!;
    public string Priority { get; set; } = "medium";
    public string StatusId { get; set; } = default!;
    public string? AssigneeId { get; set; }
    public string CreatorId { get; set; } = default!;
    public string? IterationId { get; set; }
    public string? ParentId { get; set; }
    public string? ReviewerId { get; set; }
    public Guid? RequirementTypeId { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Description { get; set; } = default!;
    public string CustomValuesJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; } = 1;
    public StatusEntity Status { get; set; } = default!;
    public UserEntity? Assignee { get; set; }
    public UserEntity Creator { get; set; } = default!;
    public UserEntity? Reviewer { get; set; }
    public RequirementTypeEntity? RequirementType { get; set; }
    public IterationEntity? Iteration { get; set; }
    public RequirementEntity? Parent { get; set; }
    public ICollection<RequirementEntity> Children { get; set; } = [];
    public ICollection<CommentEntity> Comments { get; set; } = [];
    public ICollection<HistoryEntity> History { get; set; } = [];
    public ICollection<AttachmentEntity> Attachments { get; set; } = [];
}

public sealed class CommentEntity
{
    public string? MentionsJson { get; set; }
    public Guid Id { get; set; }
    public string RequirementId { get; set; } = default!;
    public string AuthorId { get; set; } = default!;
    public string Content { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public RequirementEntity Requirement { get; set; } = default!;
    public UserEntity Author { get; set; } = default!;
}

public sealed class HistoryEntity
{
    public Guid Id { get; set; }
    public string RequirementId { get; set; } = default!;
    public string ActorId { get; set; } = default!;
    public string Action { get; set; } = default!;
    public string Detail { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public RequirementEntity Requirement { get; set; } = default!;
    public UserEntity Actor { get; set; } = default!;
}

public sealed class AttachmentEntity
{
    public bool ForComment { get; set; }
    public Guid? CommentId { get; set; }
    public Guid Id { get; set; }
    public string RequirementId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public long Size { get; set; }
    public string ContentType { get; set; } = default!;
    public string RelativePath { get; set; } = default!;
    public string Sha256 { get; set; } = default!;
    public string UploadedById { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public RequirementEntity Requirement { get; set; } = default!;
    public UserEntity UploadedBy { get; set; } = default!;
}

public sealed class UploadSessionEntity
{
    public bool ForComment { get; set; }
    public Guid Id { get; set; }
    public string RequirementId { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long TotalSize { get; set; }
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public string UploadedById { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class CustomFieldEntity
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public bool Required { get; set; }
    public bool Enabled { get; set; } = true;
    public string OptionsJson { get; set; } = "[]";
    public int SortOrder { get; set; }
}

public sealed class WorkCalendarEntity
{
    public DateOnly Date { get; set; }
    public bool IsWorkday { get; set; }
    public string? Note { get; set; }
}

public sealed class NotificationLogEntity
{
    public Guid Id { get; set; }
    public string Type { get; set; } = default!;
    public string Recipient { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public string PayloadJson { get; set; } = "{}";
    public string State { get; set; } = "pending";
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? IdempotencyKey { get; set; }
}
