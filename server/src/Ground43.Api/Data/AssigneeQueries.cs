using System.Text.Json;

namespace Ground43.Api.Data;

public static class AssigneeQueries
{
    public static IQueryable<RequirementEntity> AssignedTo(this IQueryable<RequirementEntity> query, string id)
    {
        var token = JsonSerializer.Serialize(id);
        return query.Where(x => x.AssigneeIdsJson == null ? x.AssigneeId == id : x.AssigneeIdsJson.Contains(token));
    }
}
