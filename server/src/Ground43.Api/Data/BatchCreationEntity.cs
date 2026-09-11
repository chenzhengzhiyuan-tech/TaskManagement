namespace Ground43.Api.Data;

public sealed class BatchCreationEntity
{
    public string UserId { get; set; } = "";
    public Guid RequestId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "[]";
}
