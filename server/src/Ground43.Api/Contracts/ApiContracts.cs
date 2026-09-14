using System.Text.Json;

namespace Ground43.Api.Contracts;

public sealed record LoginRequest(string Account, string Password);
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);
public sealed record UserDto(string Id, string Name, string Account, string Role, string Initials, string Color, bool WecomBound, bool Active);
public sealed record StatusDto(string Id, string Name, string Color, bool Terminal, bool Protected, int SortOrder);
public sealed record ModuleDto(Guid Id, string Name, int SortOrder);
public sealed record RequirementTypeDto(Guid Id, string Name, string Color, int SortOrder, bool Enabled);
public sealed record RequirementDefaultsDto(string? Module, string Priority, string? StatusId, string? AssigneeId, string? ReviewerId, Guid? RequirementTypeId, string IterationMode, string? IterationId, int? DueDateOffsetDays, string DescriptionTemplate);
public sealed record SystemBrandingDto(string ProjectName, string LoginBackgroundUrl, DateTimeOffset UpdatedAt);
public sealed record WorkCalendarDto(string Date, bool IsWorkday, string? Note);
public sealed record IterationDto(string Id, string Name, string StartDate, string EndDate, string State, string Goal);
public sealed record CommentMention(string UserId, string Name, int Start, int Length);
public sealed record CommentDto(string Id, string AuthorId, string Content, DateTimeOffset CreatedAt, IReadOnlyList<CommentMention> Mentions, IReadOnlyList<AttachmentDto> Attachments);
public sealed record HistoryDto(string Id, string ActorId, string Action, string Detail, DateTimeOffset CreatedAt);
public sealed record AttachmentDto(string Id, string Name, long Size, string Type, string UploadedBy, DateTimeOffset CreatedAt, string Url, string DownloadUrl);
public sealed record CustomFieldDto(string Id, string Name, string Type, bool Required, bool Enabled, string[] Options, int SortOrder);
public sealed record RequirementDto(
    string Id, string Title, string Module, string Priority, string StatusId, string? AssigneeId,
    string CreatorId, string? IterationId, string? ParentId, string? ReviewerId, Guid? RequirementTypeId, string? DueDate, string Description,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version, JsonElement CustomValues,
    IReadOnlyList<CommentDto> Comments, IReadOnlyList<HistoryDto> History, IReadOnlyList<AttachmentDto> Attachments, IReadOnlyList<string>? AssigneeIds = null);
public sealed record BootstrapDto(
    UserDto CurrentUser, IReadOnlyList<UserDto> Users, IReadOnlyList<StatusDto> Statuses,
    IReadOnlyList<IterationDto> Iterations, IReadOnlyList<RequirementDto> Requirements,
    IReadOnlyList<CustomFieldDto> CustomFields, IReadOnlyList<ModuleDto> Modules,
    IReadOnlyList<RequirementTypeDto> RequirementTypes, RequirementDefaultsDto RequirementDefaults, SystemBrandingDto Branding);

public sealed record CreateUserRequest(string Account, string Name, string Role, string Password, string? Initials, string? Color, string? WeComEmail);
public sealed record UpdateUserRequest(string? Name, string? Role, string? Password, string? Color);
public sealed record ValidateWeComUserRequest(string Email);
public sealed record BindWeComUserRequest(string Email);
public sealed record WeComUserProfileDto(string UserId, string Name, int[] Departments, int Status, bool Active);
public sealed record CreateRequirementRequest(
    string Title, string Module, string Priority, string StatusId, string? AssigneeId,
    string? IterationId, string? ParentId, string? ReviewerId, Guid? RequirementTypeId, string? DueDate, string Description, JsonElement? CustomValues, string[]? AssigneeIds = null);
public sealed record UpdateRequirementRequest(
    string? Title, string? Module, string? Priority, string? StatusId, string? AssigneeId,
    bool ClearAssignee, string? IterationId, bool ClearIteration, string? ParentId, bool ClearParent,
    string? ReviewerId, bool ClearReviewer, Guid? RequirementTypeId, bool ClearRequirementType, string? DueDate, bool ClearDueDate, string? Description, JsonElement? CustomValues, long? Version, string? Summary, string[]? AssigneeIds = null);
public sealed record CreateCommentRequest(string Content, Guid? RequestId = null, CommentMention[]? Mentions = null, Guid[]? AttachmentIds = null);
public sealed record BatchCreateRequest(Guid RequestId, CreateRequirementRequest[] Items);
public sealed record CreateModuleRequest(string Name);
public sealed record CreateRequirementTypeRequest(string Name);
public sealed record UpdateRequirementTypeRequest(string? Name, int? SortOrder, bool? Enabled);
public sealed record UpdateRequirementDefaultsRequest(string? Module, string? Priority, string? StatusId, string? AssigneeId, bool ClearAssignee, string? ReviewerId, bool ClearReviewer, Guid? RequirementTypeId, bool ClearRequirementType, string? IterationMode, string? IterationId, int? DueDateOffsetDays, bool ClearDueDateOffset, string? DescriptionTemplate);
public sealed record UpdateBrandingRequest(string? ProjectName);
public sealed record UpdateWorkCalendarRequest(bool IsWorkday, string? Note);
public sealed record RequirementTreeGroupDto(RequirementDto Root, IReadOnlyList<RequirementDto> Children, bool RootMatches);
public sealed record RequirementTreePageDto(int Page, int PageSize, int RootCount, int RequirementCount, IReadOnlyList<RequirementTreeGroupDto> Groups);
public sealed record CreateStatusRequest(string Name, string Color);
public sealed record UpdateStatusRequest(string? Name, string? Color, int? SortOrder, bool? Protected = null);
public sealed record ReorderStatusesRequest(string[] Ids);
public sealed record CreateCustomFieldRequest(string Name, string Type, bool Required, string[]? Options);
public sealed record UpdateCustomFieldRequest(string? Name, bool? Required, bool? Enabled, string[]? Options, int? SortOrder);
public sealed record CreateUploadRequest(string FileName, string ContentType, long TotalSize, int? ChunkSize, bool ForComment = false);
public sealed record CreateUploadResponse(Guid UploadId, int ChunkSize, int TotalChunks, DateTimeOffset ExpiresAt);
public sealed record CompleteUploadResponse(AttachmentDto Attachment);
public sealed record ReportSummaryDto(int Total, int Completed, int Open, int Overdue, int CompletionRate, IReadOnlyList<MemberLoadDto> MemberLoad);
public sealed record MemberLoadDto(string UserId, string Name, int OpenRequirements);
public sealed record HealthDto(string State, string Database, string Storage, DateTimeOffset Time);
public sealed record RequirementImportRowDto(
    int RowNumber, string Title, string RequirementType, string Status, string Assignee,
    string Priority, string Module, string Iteration, string DueDate, bool Valid, IReadOnlyList<string> Errors);
public sealed record RequirementImportResultDto(
    bool Preview, int TotalRows, int ValidRows, int InvalidRows, int ImportedRows,
    IReadOnlyList<RequirementImportRowDto> Rows);
