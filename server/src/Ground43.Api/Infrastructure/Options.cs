namespace Ground43.Api.Infrastructure;

public sealed class StorageOptions
{
    public string RootPath { get; set; } = "Data";
    public long MaxAttachmentBytes { get; set; } = 500L * 1024 * 1024;
    public int ChunkSizeBytes { get; set; } = 8 * 1024 * 1024;
    public int UploadSessionHours { get; set; } = 24;
    public int CapacityWarningPercent { get; set; } = 80;
    public int CapacityCriticalPercent { get; set; } = 90;
}

public sealed class AuthOptions
{
    public int SessionDays { get; set; } = 90;
}

public sealed class WeComOptions
{
    public string? CorpId { get; set; }
    public string? AgentId { get; set; }
    public string? Secret { get; set; }
    public string? AdminWebhook { get; set; }
}

public sealed class PlatformOptions
{
    public string PublicBaseUrl { get; set; } = "http://127.0.0.1:4433";
}
