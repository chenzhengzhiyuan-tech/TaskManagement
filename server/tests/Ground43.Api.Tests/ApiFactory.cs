using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ground43.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly bool _weComConfigured;
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "ground43-tests", Guid.NewGuid().ToString("N"));

    public ApiFactory(bool weComConfigured = true) => _weComConfigured = weComConfigured;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddConsole(); });
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jobs:Enabled"] = "false",
            ["Storage:RootPath"] = StorageRoot,
            ["Bootstrap:AdminPassword"] = "demo123"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
            services.RemoveAll<IWeComNotifier>();
            if (_weComConfigured) services.AddSingleton<IWeComNotifier, FakeWeComNotifier>();
            else services.AddSingleton<IWeComNotifier, UnconfiguredWeComNotifier>();
            var hosted = services.Where(x => x.ServiceType == typeof(IHostedService)).ToArray();
            foreach (var descriptor in hosted) services.Remove(descriptor);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
        if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, true);
    }
}

public sealed class FakeWeComNotifier : IWeComNotifier
{
    public bool IsConfigured => true;
    public List<(string UserId, string Content)> Messages { get; } = [];
    public int FailuresRemaining { get; set; }
    public HashSet<int> FailedAttemptNumbers { get; } = [];
    public int SendAttempts { get; private set; }
    public Task<WeComUserProfile> LookupUserAsync(string account, CancellationToken cancellationToken) => Task.FromResult(new WeComUserProfile(account.Trim(), $"企微-{account.Trim()}", [1], 1));
    public Task<WeComUserProfile> LookupUserByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var localPart = email.Trim().Split('@')[0];
        return Task.FromResult(new WeComUserProfile($"wx-{localPart}", $"企微-{localPart}", [1], 1));
    }
    public Task SendToUserAsync(string userId, string content, CancellationToken cancellationToken)
    {
        SendAttempts++;
        if (FailedAttemptNumbers.Contains(SendAttempts)) throw new HttpRequestException("test: one message part failed");
        if (FailuresRemaining > 0) { FailuresRemaining--; throw new HttpRequestException("test: enterprise API unavailable"); }
        Messages.Add((userId, content)); return Task.CompletedTask;
    }
    public Task SendAdminAlertAsync(string content, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class UnconfiguredWeComNotifier : IWeComNotifier
{
    public bool IsConfigured => false;
    public Task<WeComUserProfile> LookupUserAsync(string account, CancellationToken cancellationToken) => throw new InvalidOperationException("企微未配置时不应查询成员");
    public Task<WeComUserProfile> LookupUserByEmailAsync(string email, CancellationToken cancellationToken) => throw new InvalidOperationException("企微未配置时不应查询成员");
    public Task SendToUserAsync(string userId, string content, CancellationToken cancellationToken) => throw new InvalidOperationException("企微未配置时不应发送消息");
    public Task SendAdminAlertAsync(string content, CancellationToken cancellationToken) => Task.CompletedTask;
}



