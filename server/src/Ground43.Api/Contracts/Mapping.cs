using System.Text.Json;
using Ground43.Api.Data;

namespace Ground43.Api.Contracts;

public static class Mapping
{
    private static readonly JsonElement EmptyObject = JsonSerializer.Deserialize<JsonElement>("{}");
    public static UserDto ToDto(this UserEntity entity) => new(entity.Id, entity.Name, entity.Account, entity.Role, entity.Initials, entity.Color, !string.IsNullOrWhiteSpace(entity.WeComUserId), entity.IsActive);
    public static StatusDto ToDto(this StatusEntity entity) => new(entity.Id, entity.Name, entity.Color, entity.Terminal, entity.Protected, entity.SortOrder);
    public static ModuleDto ToDto(this ModuleEntity entity) => new(entity.Id, entity.Name, entity.SortOrder);
    public static RequirementTypeDto ToDto(this RequirementTypeEntity entity) => new(entity.Id, entity.Name, entity.Color, entity.SortOrder, entity.Enabled);
    public static RequirementDefaultsDto ToDto(this RequirementDefaultsEntity entity) => new(entity.Module, entity.Priority, entity.StatusId, entity.AssigneeId, entity.ReviewerId, entity.RequirementTypeId, entity.IterationMode, entity.IterationId, entity.DueDateOffsetDays, entity.DescriptionTemplate);
    public static SystemBrandingDto ToDto(this SystemBrandingEntity entity) => new(entity.ProjectName, entity.LoginBackgroundUrl, entity.UpdatedAt);
    public static WorkCalendarDto ToDto(this WorkCalendarEntity entity) => new(entity.Date.ToString("yyyy-MM-dd"), entity.IsWorkday, entity.Note);
    public static IterationDto ToDto(this IterationEntity entity) => new(entity.Id, entity.Name, entity.StartDate.ToString("yyyy-MM-dd"), entity.EndDate.ToString("yyyy-MM-dd"), entity.State, entity.Goal);
    public static CommentDto ToDto(this CommentEntity entity) => new(entity.Id.ToString(), entity.AuthorId, entity.Content, entity.CreatedAt);
    public static HistoryDto ToDto(this HistoryEntity entity) => new(entity.Id.ToString(), entity.ActorId, entity.Action, entity.Detail, entity.CreatedAt);
    public static AttachmentDto ToDto(this AttachmentEntity entity) => new(entity.Id.ToString(), entity.Name, entity.Size, entity.ContentType, entity.UploadedById, entity.CreatedAt, $"/api/attachments/{entity.Id}", $"/api/attachments/{entity.Id}?download=true");
    public static CustomFieldDto ToDto(this CustomFieldEntity entity) => new(entity.Id, entity.Name, entity.Type, entity.Required, entity.Enabled, DeserializeStringArray(entity.OptionsJson), entity.SortOrder);
    public static RequirementDto ToDto(this RequirementEntity entity) => new(
        entity.Id, entity.Title, entity.Module, entity.Priority, entity.StatusId, entity.AssigneeId,
        entity.CreatorId, entity.IterationId, entity.ParentId, entity.ReviewerId, entity.RequirementTypeId, entity.DueDate?.ToString("yyyy-MM-dd"), entity.Description,
        entity.CreatedAt, entity.UpdatedAt, entity.Version, ParseObject(entity.CustomValuesJson),
        entity.Comments.OrderBy(x => x.CreatedAt).Select(ToDto).ToArray(),
        entity.History.OrderByDescending(x => x.CreatedAt).Select(ToDto).ToArray(),
        entity.Attachments.OrderByDescending(x => x.CreatedAt).Select(ToDto).ToArray(), entity.GetAssigneeIds());

    public static JsonElement ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return EmptyObject.Clone();
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return EmptyObject.Clone(); }
    }

    public static string[] DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }
}
