using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Ground43.Api.Tests;
public sealed class FamilyCreationTests
{
    private static async Task Login(HttpClient client) {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { account = "admin", password = "demo123" });
        var auth = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
    }
    private static CreateRequirementRequest Item(Guid type, string title = "父子事务验证") => new(title, "UI", "medium", "todo", null, null, null, null, type, null, "测试描述", null, []);
    [Fact]
    public async Task Family_CreatesAndLinksAtomically_RetryDoesNotDuplicate()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await Login(client);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var type = await db.RequirementTypes.Where(x => x.Enabled).Select(x => x.Id).FirstAsync();
        var oldResponse = await client.PostAsJsonAsync("/api/requirements", Item(type, "已有待绑定"));
        var old = (await oldResponse.Content.ReadFromJsonAsync<RequirementDto>())!;
        var before = await db.Requirements.CountAsync();
        var body = new CreateFamilyRequest(Guid.NewGuid(), Item(type), [Item(type, "子一"), Item(type, "子二")], [old.Id]);
        var response = await client.PostAsJsonAsync("/api/requirements/family", body); response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); var parent = json.GetProperty("parentId").GetString();
        Assert.Equal(before + 3, await db.Requirements.CountAsync());
        Assert.Equal(3, await db.Requirements.CountAsync(x => x.ParentId == parent));
        var retry = await client.PostAsJsonAsync("/api/requirements/family", body); retry.EnsureSuccessStatusCode();
        Assert.Equal(before + 3, await db.Requirements.CountAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/requirements/family", body with { Parent = Item(type, "不同内容") })).StatusCode);
        var single = await db.Requirements.AsNoTracking().SingleAsync(x => x.Id == parent);
        Assert.Null(single.IterationId); Assert.Null(single.ReviewerId);
    }
    [Fact]
    public async Task InvalidLaterChild_RollsBackParentEarlierChildHistoryNotificationsAndReceipt()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await Login(client);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var type = await db.RequirementTypes.Where(x => x.Enabled).Select(x => x.Id).FirstAsync();
        var counts = (await db.Requirements.CountAsync(), await db.History.CountAsync(), await db.NotificationLogs.CountAsync(), await db.BatchCreations.CountAsync());
        var body = new CreateFamilyRequest(Guid.NewGuid(), Item(type), [Item(type), Item(type) with { Priority = "invalid" }], []);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/requirements/family", body)).StatusCode);
        Assert.Equal(counts, (await db.Requirements.CountAsync(), await db.History.CountAsync(), await db.NotificationLogs.CountAsync(), await db.BatchCreations.CountAsync()));
        (await client.PostAsJsonAsync("/api/requirements/family", body with { Children = [Item(type)] })).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task BoundChildRejected_AndBatchLinkRetriesAreIdempotent()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await Login(client);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var type = await db.RequirementTypes.Where(x => x.Enabled).Select(x => x.Id).FirstAsync();
        var p = (await (await client.PostAsJsonAsync("/api/requirements", Item(type))).Content.ReadFromJsonAsync<RequirementDto>())!;
        var c = (await (await client.PostAsJsonAsync("/api/requirements", Item(type))).Content.ReadFromJsonAsync<RequirementDto>())!;
        (await client.PostAsJsonAsync($"/api/requirements/{p.Id}/children", new LinkChildrenRequest([c.Id]))).EnsureSuccessStatusCode();
        var history = await db.History.CountAsync();
        (await client.PostAsJsonAsync($"/api/requirements/{p.Id}/children", new LinkChildrenRequest([c.Id]))).EnsureSuccessStatusCode();
        Assert.Equal(history, await db.History.CountAsync());
        var count = await db.Requirements.CountAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/requirements/family", new CreateFamilyRequest(Guid.NewGuid(), Item(type), [], [c.Id]))).StatusCode);
        Assert.Equal(count, await db.Requirements.CountAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/requirements/family", new CreateFamilyRequest(Guid.NewGuid(), Item(type) with { ParentId = p.Id }, [Item(type)], []))).StatusCode);
    }
}
