using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Ground43.Api.Data;
using Ground43.Api.Hosted;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ground43.Api.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task BatchCreation_IsAtomicAndReplaySafe()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        await LoginAsync(client, "admin");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.Requirements.CountAsync();
        var type = await db.RequirementTypes.Select(x => x.Id).FirstAsync();
        object Row(string title) => new { title, description = "批量测试描述", module = "UI", priority = "high", statusId = "todo", requirementTypeId = type };
        var bad = await client.PostAsJsonAsync("/api/requirements/batch", new { requestId = Guid.NewGuid(), items = new[] { Row("正常行"), Row("") } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(before, await db.Requirements.CountAsync());
        Assert.Equal(0, await db.BatchCreations.CountAsync());
        var key = Guid.NewGuid(); var request = new { requestId = key, items = new[] { Row("第一条"), Row("第二条") } };
        var first = await client.PostAsJsonAsync("/api/requirements/batch", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var replay = await client.PostAsJsonAsync("/api/requirements/batch", request);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(before + 2, await db.Requirements.CountAsync());
        var conflict = await client.PostAsJsonAsync("/api/requirements/batch", new { requestId = key, items = new[] { Row("变更内容") } });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }
    [Fact]
    public async Task Tree_MineIncludesAssignedAndPendingReviewsBeforePagination()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        await LoginAsync(client, "admin");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var items = await db.Requirements.OrderBy(x => x.Id).ToListAsync();
        foreach (var item in items) { item.AssigneeId = null; item.ReviewerId = null; item.ParentId = null; }
        items[0].AssigneeId = "u-admin"; items[0].StatusId = "completed";
        items[1].ReviewerId = "u-admin"; items[1].StatusId = "review";
        items[2].ReviewerId = "u-admin"; items[2].StatusId = "todo";
        items[3].AssigneeId = "u-admin"; items[3].ReviewerId = "u-admin"; items[3].StatusId = "review";
        await db.SaveChangesAsync();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/requirements/tree?mine=true&pageSize=1");
        Assert.Equal(3, result.GetProperty("requirementCount").GetInt32());
        Assert.Equal(3, result.GetProperty("rootCount").GetInt32());
        Assert.Equal(1, result.GetProperty("groups").GetArrayLength());
        var reviews = await client.GetFromJsonAsync<JsonElement>("/api/requirements/tree?mine=true&statusId=review");
        Assert.Equal(2, reviews.GetProperty("requirementCount").GetInt32());
    }
    [Fact]
    public async Task Health_Is_Public()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_Rejects_Wrong_Password_And_Returns_Session()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        var rejected = await client.PostAsJsonAsync("/api/auth/login", new { account = "admin", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        var accepted = await client.PostAsJsonAsync("/api/auth/login", new { account = "admin", password = "demo123" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var json = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Bootstrap_Requires_Authentication_And_Returns_Client_Shape()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/bootstrap")).StatusCode);
        await LoginAsync(client, "admin");
        var rebound = await client.PostAsJsonAsync("/api/users/u-admin/wecom/bind", new { email = "admin@example.com" });
        Assert.Equal(HttpStatusCode.OK, rebound.StatusCode);
        var bootstrap = await client.GetFromJsonAsync<JsonElement>("/api/bootstrap");
        Assert.Equal("u-admin", bootstrap.GetProperty("currentUser").GetProperty("id").GetString());
        Assert.True(bootstrap.GetProperty("requirements").GetArrayLength() >= 4);
        Assert.True(bootstrap.GetProperty("modules").GetArrayLength() >= 10);
    }

    [Fact]
    public async Task Developer_Cannot_Move_To_Protected_Status()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "member-a");
        var current = await client.GetFromJsonAsync<JsonElement>("/api/requirements/REQ-0048");
        var version = current.GetProperty("version").GetInt64();
        var response = await client.PatchAsJsonAsync("/api/requirements/REQ-0048", new { statusId = "completed", version });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Parent_Cannot_Complete_Or_Close_Until_All_Children_Are_Final()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "admin");
        var parent = await client.GetFromJsonAsync<JsonElement>("/api/requirements/REQ-0045");
        var blocked = await client.PatchAsJsonAsync("/api/requirements/REQ-0045", new
        {
            statusId = "completed", version = parent.GetProperty("version").GetInt64()
        });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using (var error = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync()))
            Assert.Equal("父任务仍有 1 个未完成的子任务，请先将所有子任务设置为“已完成”或“已关闭”", error.RootElement.GetProperty("message").GetString());

        var child = await client.GetFromJsonAsync<JsonElement>("/api/requirements/REQ-0041");
        var childUpdate = await client.PatchAsJsonAsync("/api/requirements/REQ-0041", new
        {
            statusId = "closed", version = child.GetProperty("version").GetInt64()
        });
        Assert.Equal(HttpStatusCode.OK, childUpdate.StatusCode);

        parent = await client.GetFromJsonAsync<JsonElement>("/api/requirements/REQ-0045");
        var completed = await client.PatchAsJsonAsync("/api/requirements/REQ-0045", new
        {
            statusId = "completed", version = parent.GetProperty("version").GetInt64()
        });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    [Fact]
    public async Task Admin_Can_Create_Module_And_Requirement_Child_Link()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "admin");
        var moduleResponse = await client.PostAsJsonAsync("/api/modules", new { name = "音频" });
        Assert.Equal(HttpStatusCode.Created, moduleResponse.StatusCode);
        var create = await client.PostAsJsonAsync("/api/requirements", new
        {
            title = "服务端父子关系测试", module = "音频", priority = "medium", statusId = "todo",
            assigneeId = (string?)null, iterationId = "it-current", parentId = "REQ-0045", dueDate = "2026-08-28",
            description = "验证服务端创建子需求。", customValues = new { }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var json = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        Assert.Equal("REQ-0045", json.RootElement.GetProperty("parentId").GetString());
    }

    [Fact]
    public async Task Admin_Delete_Parent_Promotes_Child()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "admin");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/requirements/REQ-0045")).StatusCode);
        var child = await client.GetFromJsonAsync<JsonElement>("/api/requirements/REQ-0041");
        Assert.Equal(JsonValueKind.Null, child.GetProperty("parentId").ValueKind);
    }

    [Fact]
    public async Task Chunked_Image_Upload_Completes_And_Downloads()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "admin");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFElEQVR42mP8z8AARAwMjIwgCjAAAP//AwBfNwQCBFJzNwAAAABJRU5ErkJggg==");
        var created = await client.PostAsJsonAsync("/api/requirements/REQ-0048/attachments/uploads", new { fileName = "test.png", contentType = "image/png", totalSize = png.Length, chunkSize = 1024 * 1024 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var uploadJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var uploadId = uploadJson.RootElement.GetProperty("uploadId").GetGuid();
        using var chunk = new ByteArrayContent(png); chunk.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/attachments/uploads/{uploadId}/chunks/0", chunk)).StatusCode);
        var completed = await client.PostAsync($"/api/attachments/uploads/{uploadId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var completeJson = JsonDocument.Parse(await completed.Content.ReadAsStringAsync());
        var attachmentId = completeJson.RootElement.GetProperty("attachment").GetProperty("id").GetString();
        var download = await client.GetAsync($"/api/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(png, await download.Content.ReadAsByteArrayAsync());
        using var developer = factory.CreateClient(); await LoginAsync(developer, "member-a");
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.DeleteAsync($"/api/attachments/{attachmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/attachments/{attachmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/attachments/{attachmentId}")).StatusCode);
    }


    [Fact]
    public async Task Admin_Can_Create_Reset_And_Delete_Member()
    {
        using var factory = new ApiFactory(weComConfigured: false);
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin");
        var created = await client.PostAsJsonAsync("/api/users", new { account = "newdev", name = "新成员", role = "developer", password = "Initial123!", initials = "新", color = "#0a84ff", weComEmail = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("wecomBound").GetBoolean());
        var id = json.RootElement.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/users/{id}", new { password = "Reset456!" })).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login", new { account = "新成员", password = "Reset456!" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/users/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { account = "新成员", password = "Reset456!" })).StatusCode);
    }

    [Fact]
    public async Task Admin_Can_Validate_Email_And_Bind_Canonical_WeCom_User_Without_Changing_Platform_Account()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin");

        var validation = await client.PostAsJsonAsync("/api/users/wecom/validate", new { email = "bounddev@example.com" });
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        using (var validationJson = JsonDocument.Parse(await validation.Content.ReadAsStringAsync()))
            Assert.Equal("wx-bounddev", validationJson.RootElement.GetProperty("userId").GetString());

        var created = await client.PostAsJsonAsync("/api/users", new { account = "bounddev", name = "已绑定成员", role = "developer", password = "Initial123!", weComEmail = "bounddev@example.com" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal("bounddev", json.RootElement.GetProperty("account").GetString());
        Assert.True(json.RootElement.GetProperty("wecomBound").GetBoolean());

        var id = json.RootElement.GetProperty("id").GetString();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.Users.SingleAsync(x => x.Id == id);
        Assert.Equal("bounddev", member.Account);
        Assert.Equal("bounddev@example.com", member.WeComEmail);
        Assert.Equal("wx-bounddev", member.WeComUserId);
    }

    [Fact]
    public async Task Admin_Can_Bind_And_Unbind_Existing_Member_By_Email_And_Duplicate_Binding_Is_Rejected()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin");

        var bound = await client.PostAsJsonAsync("/api/users/u-admin/wecom/bind", new { email = "admin@example.com" });
        Assert.Equal(HttpStatusCode.OK, bound.StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/users/u-liu/wecom/bind", new { email = "admin@example.com" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await db.Users.SingleAsync(x => x.Id == "u-admin");
            Assert.Equal("admin", admin.Account);
            Assert.Equal("admin@example.com", admin.WeComEmail);
            Assert.Equal("wx-admin", admin.WeComUserId);
        }

        var unbound = await client.DeleteAsync("/api/users/u-admin/wecom/bind");
        Assert.Equal(HttpStatusCode.OK, unbound.StatusCode);
        using var json = JsonDocument.Parse(await unbound.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("wecomBound").GetBoolean());
    }

    [Fact]
    public async Task Admin_Can_Preview_And_Atomically_Import_Independent_Requirements()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "admin");
        const string csv = "\ufeff需求编号,标题,父需求,需求单类型,状态,处理人,验收人,优先级,模块,迭代,期望完成时间,需求描述\r\nOLD-1,批量导入一,REQ-0045,周版本反馈,未开始,示例成员甲,示例管理员,高,UI,需求池,2026-09-01,第一条描述\r\nOLD-2,批量导入二,,周版本反馈,进行中,,,中,服务器,20260824-20260828,,第二条描述";
        using var previewContent = new MultipartFormDataContent(); previewContent.Add(new StringContent(csv, System.Text.Encoding.UTF8), "file", "requirements.csv");
        var previewResponse = await client.PostAsync("/api/requirements/import?commit=false", previewContent);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        using (var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(2, preview.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(0, preview.RootElement.GetProperty("invalidRows").GetInt32());
        }

        using var commitContent = new MultipartFormDataContent(); commitContent.Add(new StringContent(csv, System.Text.Encoding.UTF8), "file", "requirements.csv");
        var commitResponse = await client.PostAsync("/api/requirements/import?commit=true", commitContent);
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);
        using var committed = JsonDocument.Parse(await commitResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, committed.RootElement.GetProperty("importedRows").GetInt32());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var imported = await db.Requirements.Where(x => x.Title.StartsWith("批量导入")).ToListAsync();
        Assert.Equal(2, imported.Count); Assert.All(imported, item => Assert.Null(item.ParentId));
    }

    [Fact]
    public async Task Import_With_Invalid_Row_Creates_Nothing()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "admin");
        const string csv = "标题,需求单类型,状态,优先级,模块,需求描述\r\n有效行,周版本反馈,未开始,高,UI,描述\r\n错误行,周版本反馈,不存在的状态,中,UI,描述";
        using var content = new MultipartFormDataContent(); content.Add(new StringContent(csv, System.Text.Encoding.UTF8), "file", "invalid.csv");
        var response = await client.PostAsync("/api/requirements/import?commit=true", content);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Requirements.AnyAsync(x => x.Title == "有效行" || x.Title == "错误行"));
    }

    [Fact]
    public async Task Admin_Can_Preview_Xlsx_Export_Format()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "admin");
        using var workbook = new XLWorkbook(); var sheet = workbook.AddWorksheet("需求");
        var headers = new[] { "需求编号", "标题", "父需求", "需求单类型", "状态", "处理人", "验收人", "优先级", "模块", "迭代", "期望完成时间", "需求描述" };
        for (var index = 0; index < headers.Length; index++) sheet.Cell(1, index + 1).Value = headers[index];
        sheet.Cell(2, 1).Value = "OLD-100"; sheet.Cell(2, 2).Value = "Excel 导入需求"; sheet.Cell(2, 3).Value = "REQ-0045";
        sheet.Cell(2, 4).Value = "周版本反馈"; sheet.Cell(2, 5).Value = "未开始"; sheet.Cell(2, 6).Value = "示例成员甲";
        sheet.Cell(2, 8).Value = "高"; sheet.Cell(2, 9).Value = "UI"; sheet.Cell(2, 10).Value = "需求池";
        sheet.Cell(2, 11).Value = new DateTime(2026, 9, 2); sheet.Cell(2, 12).Value = "Excel 描述";
        await using var stream = new MemoryStream(); workbook.SaveAs(stream);
        using var content = new MultipartFormDataContent(); content.Add(new ByteArrayContent(stream.ToArray()), "file", "requirements.xlsx");
        var response = await client.PostAsync("/api/requirements/import?commit=false", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("validRows").GetInt32());
        Assert.Equal("2026-09-02", json.RootElement.GetProperty("rows")[0].GetProperty("dueDate").GetString());
    }

    [Fact]
    public void Daily_Summary_Contains_All_Open_Items_Highlights_And_Deep_Links()
    {
        var today = new DateOnly(2026, 8, 28);
        var items = new[]
        {
            new RequirementEntity { Id = "REQ-0101", Title = "已超期事项", DueDate = today.AddDays(-1) },
            new RequirementEntity { Id = "REQ-0102", Title = "明日到期事项", DueDate = today.AddDays(1) },
            new RequirementEntity { Id = "REQ-0103", Title = "未来事项", DueDate = today.AddDays(7) },
            new RequirementEntity { Id = "REQ-0104", Title = "未设日期事项", DueDate = null }
        };
        var content = string.Join("\n", PlatformMaintenanceService.BuildDailyMessages("测试成员", today, items, "http://127.0.0.1:4433"));
        Assert.Contains("【已超期 1 天】REQ-0101", content); Assert.Contains("【明日截止】REQ-0102", content);
        Assert.Contains("【未完成】REQ-0103", content); Assert.Contains("【未完成】REQ-0104", content);
        Assert.All(items, item => Assert.Contains($"http://127.0.0.1:4433/?requirement={item.Id}", content));
    }

    [Fact]
    public async Task Bootstrap_Contains_Types_Defaults_Branding_And_Official_2026_Calendar()
    {
        using var factory=new ApiFactory();using var client=factory.CreateClient();await LoginAsync(client,"admin");
        var bootstrap=await client.GetFromJsonAsync<JsonElement>("/api/bootstrap");Assert.Equal("G43",bootstrap.GetProperty("branding").GetProperty("projectName").GetString());Assert.Equal(3,bootstrap.GetProperty("requirementTypes").GetArrayLength());
        var calendar=await client.GetFromJsonAsync<JsonElement>("/api/work-calendar?year=2026");Assert.True(calendar.GetArrayLength()>20);Assert.Contains(calendar.EnumerateArray(),x=>x.GetProperty("date").GetString()=="2026-02-14"&&x.GetProperty("isWorkday").GetBoolean());
    }

    [Fact]
    public async Task Requirement_Tree_Api_Paginates_Roots_And_Defaults_Are_Applied()
    {
        using var factory=new ApiFactory();using var client=factory.CreateClient();await LoginAsync(client,"admin");
        var tree=await client.GetFromJsonAsync<JsonElement>("/api/requirements/tree?page=1&pageSize=2&sort=updated");Assert.Equal(2,tree.GetProperty("groups").GetArrayLength());
        var created=await client.PostAsJsonAsync("/api/requirements",new{title="默认值需求",module="",priority="",statusId="",assigneeId=(string?)null,iterationId=(string?)null,parentId=(string?)null,reviewerId=(string?)null,requirementTypeId=(Guid?)null,dueDate=(string?)null,description="默认值测试",customValues=new{}});Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        using var json=JsonDocument.Parse(await created.Content.ReadAsStringAsync());Assert.Equal("medium",json.RootElement.GetProperty("priority").GetString());Assert.NotEqual(JsonValueKind.Null,json.RootElement.GetProperty("requirementTypeId").ValueKind);
    }
    [Fact]
    public async Task Admin_Delete_User_Physically_Removes_Account_And_Preserves_History()
    {
        using var factory = new ApiFactory();
        using var deletedSessionClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        await LoginAsync(deletedSessionClient, "member-a");
        await LoginAsync(adminClient, "admin");

        var response = await adminClient.DeleteAsync("/api/users/u-liu");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AnyAsync(x => x.Id == "u-liu"));
            var historicalIdentity = await db.Users.SingleAsync(x => x.Id == SystemUsers.DeletedId);
            Assert.False(historicalIdentity.IsActive);
        }

        var requirement = await adminClient.GetFromJsonAsync<JsonElement>("/api/requirements/REQ-0048");
        Assert.Equal(JsonValueKind.Null, requirement.GetProperty("assigneeId").ValueKind);
        Assert.Contains(requirement.GetProperty("history").EnumerateArray(), entry => entry.GetProperty("actorId").GetString() == SystemUsers.DeletedId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await deletedSessionClient.GetAsync("/api/bootstrap")).StatusCode);
    }
    [Fact]
    public async Task Requirement_Type_Colors_Remain_Unique_Beyond_Fixed_Palette()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin");
        var colors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bootstrap = await client.GetFromJsonAsync<JsonElement>("/api/bootstrap");
        foreach (var item in bootstrap.GetProperty("requirementTypes").EnumerateArray()) colors.Add(item.GetProperty("color").GetString()!);
        for (var index = 0; index < 12; index++)
        {
            var response = await client.PostAsJsonAsync("/api/requirement-types", new { name = $"自动配色{index}" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(colors.Add(json.RootElement.GetProperty("color").GetString()!));
        }
    }
    [Fact]
    public async Task Settings_RenameModule_PreservesReferences_AndReordersStatuses()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "admin");
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var module = await db.Modules.SingleAsync(x => x.Name == "UI");
        var ids = await db.Requirements.Where(x => x.Module == "UI").Select(x => x.Id).ToListAsync();
        (await client.PatchAsJsonAsync($"/api/modules/{module.Id}", new { name = "界面" })).EnsureSuccessStatusCode();
        Assert.Equal(ids.Count, await db.Requirements.CountAsync(x => ids.Contains(x.Id) && x.Module == "界面"));
        var statuses = await client.GetFromJsonAsync<JsonElement>("/api/statuses");
        var order = statuses.EnumerateArray().Select(x => x.GetProperty("id").GetString()!).Reverse().ToArray();
        (await client.PutAsJsonAsync("/api/statuses/order", new { ids = order })).EnsureSuccessStatusCode();
        var reordered = await client.GetFromJsonAsync<JsonElement>("/api/statuses");
        Assert.Equal(order, reordered.EnumerateArray().Select(x => x.GetProperty("id").GetString()!));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/statuses/order", new { ids = order.Take(1) })).StatusCode);
    }

    [Fact]
    public async Task StatusProtection_IsConfigurable_AndDoesNotPreventAdminRenameOrDelete()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "admin");
        var created = await client.PostAsJsonAsync("/api/statuses", new { name = "可配置状态", color = "#123456" });
        var status = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = status.GetProperty("id").GetString();
        (await client.PatchAsJsonAsync($"/api/statuses/{id}", new { name = "改名后状态", @protected = true })).EnsureSuccessStatusCode();
        using var developer = factory.CreateClient(); await LoginAsync(developer, "member-a");
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.PatchAsJsonAsync("/api/requirements/REQ-0048", new { statusId = id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.PatchAsJsonAsync($"/api/statuses/{id}", new { @protected = false })).StatusCode);
        (await client.PatchAsJsonAsync($"/api/statuses/{id}", new { @protected = false })).EnsureSuccessStatusCode();
        (await developer.PatchAsJsonAsync("/api/requirements/REQ-0048", new { statusId = id })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync("/api/requirements/REQ-0048", new { statusId = "todo" })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/statuses/{id}", new { @protected = true })).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/statuses/{id}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Tree_MultiFiltersUseOrWithinAndAcrossFields()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient(); await LoginAsync(client, "admin");
        var result = await client.GetFromJsonAsync<JsonElement>("/api/requirements/tree?priority=urgent,high&assigneeId=u-liu,u-chen&statusId=in_progress,review&pageSize=50");
        var roots = result.GetProperty("groups").EnumerateArray().Select(x => x.GetProperty("root")).ToArray();
        Assert.Equal(2, roots.Length);
        Assert.All(roots, root => {
            Assert.Contains(root.GetProperty("priority").GetString(), new[] { "urgent", "high" });
            Assert.Contains(root.GetProperty("assigneeId").GetString(), new[] { "u-liu", "u-chen" });
            Assert.Contains(root.GetProperty("statusId").GetString(), new[] { "in_progress", "review" });
        });
    }

    private static async Task LoginAsync(HttpClient client, string account)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { account, password = "demo123" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
    }
}
