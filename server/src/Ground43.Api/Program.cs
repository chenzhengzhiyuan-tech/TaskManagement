using Ground43.Api.Data;
using Ground43.Api.Endpoints;
using Ground43.Api.Hosted;
using Ground43.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables(prefix: "GROUND43_");
builder.Host.UseWindowsService(options => options.ServiceName = "Ground43 Requirement Platform");
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<WeComOptions>(builder.Configuration.GetSection("WeCom"));
builder.Services.Configure<PlatformOptions>(builder.Configuration.GetSection("Platform"));
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 20L * 1024 * 1024);

var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=Data/ground43.db";
if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var sqlite = new SqliteConnectionStringBuilder(connectionString);
    if (!Path.IsPathRooted(sqlite.DataSource)) sqlite.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource));
    var directory = Path.GetDirectoryName(sqlite.DataSource);
    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    connectionString = sqlite.ToString();
}
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)) options.UseNpgsql(connectionString);
    else options.UseSqlite(connectionString);
});

builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<RequirementCodeService>();
builder.Services.AddScoped<RequirementImportService>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<WorkdayService>();
builder.Services.AddScoped<DistributedJobLock>();
builder.Services.AddScoped<IterationRolloverService>();
builder.Services.AddScoped<ReviewNotificationService>();
builder.Services.AddScoped<AssignmentNotificationService>();
builder.Services.AddScoped<CommentNotificationService>();
builder.Services.AddScoped<CommentService>();
builder.Services.AddSingleton<SessionTokenService>();
builder.Services.AddSingleton<AttachmentStorage>();
builder.Services.AddHttpClient<IWeComNotifier, WeComNotifier>();
builder.Services.AddAuthentication(SessionAuthenticationHandler.AuthenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.AuthenticationScheme, null);
builder.Services.AddAuthorizationBuilder().AddPolicy("Admin", policy => policy.RequireRole(Roles.Admin));
if (builder.Configuration.GetValue("Jobs:Enabled", true))
{
    builder.Services.AddHostedService<IterationMaintenanceService>();
    builder.Services.AddHostedService<PlatformMaintenanceService>();
    builder.Services.AddHostedService<ReviewNotificationWorker>();
}
builder.Services.AddCors(options => options.AddPolicy("DevelopmentClient", policy => policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.MapOpenApi(); app.UseCors("DevelopmentClient"); }
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", async (AppDbContext db, AttachmentStorage storage, CancellationToken ct) =>
{
    var database = await db.Database.CanConnectAsync(ct) ? "ok" : "unavailable";
    var storageState = Directory.Exists(storage.RootPath) ? "ok" : "unavailable";
    return Results.Ok(new Ground43.Api.Contracts.HealthDto(database == "ok" && storageState == "ok" ? "ok" : "degraded", database, storageState, DateTimeOffset.UtcNow));
}).AllowAnonymous().WithTags("Health");

app.MapAuthEndpoints();
app.MapBootstrapEndpoints();
app.MapUserEndpoints();
app.MapRequirementEndpoints();
app.MapRequirementImportEndpoints();
app.MapConfigurationEndpoints();
app.MapIterationEndpoints();
app.MapReportEndpoints();
app.MapAttachmentEndpoints();
app.MapSystemEndpoints();
app.MapFallbackToFile("index.html").AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

app.Run();

public partial class Program;


