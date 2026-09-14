namespace Ground43.Api.Infrastructure;

internal static class RequirementSort
{
    public static int StatusRank(string id) => id switch
    { "todo" => 0, "in_progress" => 1, "review" => 2, "paused" => 3, "backlog" => 4, "completed" => 5, "closed" => 6, _ => 7 };
    public static int PriorityRank(string priority) => priority switch
    { "urgent" => 0, "high" => 1, "medium" => 2, _ => 3 };
    public static long Number(string id) => long.TryParse(id.StartsWith("REQ-", StringComparison.OrdinalIgnoreCase) ? id[4..] : id, out var number) ? number : long.MaxValue;
}
