using System.Net.Http.Json;
using System.Text.Json;
using Ground43.Api.Data;
using Microsoft.Extensions.Options;

namespace Ground43.Api.Infrastructure;

public interface IWeComNotifier
{
    bool IsConfigured { get; }
    Task<WeComUserProfile> LookupUserAsync(string account, CancellationToken cancellationToken);
    Task<WeComUserProfile> LookupUserByEmailAsync(string email, CancellationToken cancellationToken);
    Task SendToUserAsync(string userId, string content, CancellationToken cancellationToken);
    Task SendAdminAlertAsync(string content, CancellationToken cancellationToken);
}

public sealed record WeComUserProfile(string UserId, string Name, int[] Departments, int Status);

public sealed class WeComNotifier(HttpClient http, IOptions<WeComOptions> options, ILogger<WeComNotifier> logger) : IWeComNotifier
{
    private readonly WeComOptions _options = options.Value;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.CorpId)
        && !string.IsNullOrWhiteSpace(_options.Secret)
        && int.TryParse(_options.AgentId, out _);

    public async Task<WeComUserProfile> LookupUserAsync(string account, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("企业微信自建应用尚未配置");
        var token = await AccessTokenAsync(cancellationToken);
        JsonElement result;
        try
        {
            result = await http.GetFromJsonAsync<JsonElement>($"https://qyapi.weixin.qq.com/cgi-bin/user/get?access_token={Uri.EscapeDataString(token)}&userid={Uri.EscapeDataString(account.Trim())}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("无法连接企业微信，请检查网络和可信 IP 配置", ex);
        }
        ThrowIfWeComFailed(result, "成员校验");
        var userId = result.GetProperty("userid").GetString();
        var name = result.GetProperty("name").GetString();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("企业微信返回的成员数据不完整");
        var departments = result.TryGetProperty("department", out var department) && department.ValueKind == JsonValueKind.Array
            ? department.EnumerateArray().Select(x => x.GetInt32()).ToArray()
            : [];
        var status = result.TryGetProperty("status", out var statusValue) ? statusValue.GetInt32() : 0;
        return new WeComUserProfile(userId, name, departments, status);
    }

    public async Task<WeComUserProfile> LookupUserByEmailAsync(string email, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("企业微信自建应用尚未配置");
        var token = await AccessTokenAsync(cancellationToken);
        JsonElement result;
        try
        {
            var response = await http.PostAsJsonAsync(
                $"https://qyapi.weixin.qq.com/cgi-bin/user/get_userid_by_email?access_token={Uri.EscapeDataString(token)}",
                new { email = email.Trim(), email_type = 2 },
                cancellationToken);
            response.EnsureSuccessStatusCode();
            result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "WeCom email lookup request failed");
            var status = ex.StatusCode is null ? string.Empty : $"（HTTP {(int)ex.StatusCode.Value}）";
            throw new InvalidOperationException($"企业微信邮箱查询请求失败{status}，请检查接口地址、网络和可信 IP 配置", ex);
        }
        ThrowIfWeComFailed(result, "邮箱查询");
        var userId = result.TryGetProperty("userid", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(userId)) throw new InvalidOperationException("企业微信未返回 userid，请确认邮箱属于当前企业成员");
        return await LookupUserAsync(userId, cancellationToken);
    }

    public async Task SendToUserAsync(string userId, string content, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("企业微信自建应用尚未配置，消息未发送");
        }
        var token = await AccessTokenAsync(cancellationToken);
        var payload = new { touser = userId, msgtype = "text", agentid = int.Parse(_options.AgentId!), text = new { content }, safe = 0 };
        var response = await http.PostAsJsonAsync($"https://qyapi.weixin.qq.com/cgi-bin/message/send?access_token={Uri.EscapeDataString(token)}", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        ThrowIfWeComFailed(result, "消息发送");
        if (result.TryGetProperty("invaliduser", out var invalidUser) && !string.IsNullOrWhiteSpace(invalidUser.GetString()))
            throw new InvalidOperationException("企业微信消息接收人无效，请检查成员绑定和自建应用可见范围");
        if (result.TryGetProperty("unlicenseduser", out var unlicensedUser) && !string.IsNullOrWhiteSpace(unlicensedUser.GetString()))
            throw new InvalidOperationException("企业微信消息接收人未获得应用许可");
    }

    public async Task SendAdminAlertAsync(string content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AdminWebhook)) { logger.LogWarning("Admin webhook is not configured: {Content}", content); return; }
        var response = await http.PostAsJsonAsync(_options.AdminWebhook, new { msgtype = "text", text = new { content } }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> AccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return _accessToken;
        var result = await http.GetFromJsonAsync<JsonElement>($"https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid={Uri.EscapeDataString(_options.CorpId!)}&corpsecret={Uri.EscapeDataString(_options.Secret!)}", cancellationToken);
        ThrowIfWeComFailed(result, "访问令牌获取");
        _accessToken = result.GetProperty("access_token").GetString()!;
        _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(result.GetProperty("expires_in").GetInt32());
        return _accessToken;
    }

    private static void ThrowIfWeComFailed(JsonElement result, string operation)
    {
        if (!result.TryGetProperty("errcode", out var code) || code.GetInt32() == 0) return;
        var message = result.TryGetProperty("errmsg", out var error) ? error.GetString() : "未知错误";
        throw new InvalidOperationException($"企业微信{operation}失败（{code.GetInt32()}）：{message}");
    }
}
