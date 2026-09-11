using System.Text.Json;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Data;

public sealed class DatabaseInitializer(AppDbContext db, PasswordService passwordService, IConfiguration configuration, IHostEnvironment environment)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (db.Database.IsRelational() && !db.Database.IsSqlite()) await db.Database.MigrateAsync(cancellationToken);
        else { await db.Database.EnsureCreatedAsync(cancellationToken); await UpgradeSqliteSchemaAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "BatchCreations" ("UserId" TEXT NOT NULL, "RequestId" TEXT NOT NULL, "PayloadHash" TEXT NOT NULL, "ResultJson" TEXT NOT NULL, PRIMARY KEY ("UserId", "RequestId"))""", cancellationToken);
        }
        if (!await db.Users.AnyAsync(cancellationToken))
        {
        var now = DateTimeOffset.UtcNow;
        var admin = NewUser("u-admin", "admin", "示例管理员", Roles.Admin, "陈", "#f5f5f7", now);
        var liu = NewUser("u-liu", "member-a", "示例成员甲", Roles.Developer, "刘", "#0a84ff", now);
        var wang = NewUser("u-wang", "member-b", "示例成员乙", Roles.Developer, "王", "#bf5af2", now);
        var chen = NewUser("u-chen", "member-c", "示例成员丙", Roles.Developer, "屿", "#30d158", now);
        var zhao = NewUser("u-zhao", "member-d", "示例成员丁", Roles.Developer, "赵", "#ff9f0a", now);
        var bootstrapPassword = configuration["Bootstrap:AdminPassword"] ?? (environment.IsDevelopment() ? "demo123" : null);
        if (string.IsNullOrWhiteSpace(bootstrapPassword)) throw new InvalidOperationException("Production requires Bootstrap:AdminPassword or GROUND43_Bootstrap__AdminPassword.");
        foreach (var user in new[] { admin, liu, wang, chen, zhao }) user.PasswordHash = passwordService.Hash(user, bootstrapPassword);
        db.Users.AddRange(admin, liu, wang, chen, zhao);

        db.Statuses.AddRange(
            new StatusEntity { Id = "backlog", Name = "需求池", Color = "#8e8e93", SortOrder = 1 },
            new StatusEntity { Id = "todo", Name = "未开始", Color = "#64d2ff", SortOrder = 2 },
            new StatusEntity { Id = "in_progress", Name = "进行中", Color = "#0a84ff", SortOrder = 3 },
            new StatusEntity { Id = "review", Name = "待验收", Color = "#ff9f0a", SortOrder = 4 },
            new StatusEntity { Id = "completed", Name = "已完成", Color = "#30d158", SortOrder = 5, Terminal = true, Protected = true },
            new StatusEntity { Id = "closed", Name = "已关闭", Color = "#8e8e93", SortOrder = 6, Terminal = true, Protected = true },
            new StatusEntity { Id = "online", Name = "已上线", Color = "#5e5ce6", SortOrder = 7, Terminal = true },
            new StatusEntity { Id = "paused", Name = "暂停", Color = "#ff453a", SortOrder = 8 });

        var modules = new[] { "通用", "文档", "测试", "性能", "交互", "数据", "工具", "UI", "服务器", "体验", "其他" };
        db.Modules.AddRange(modules.Select((name, index) => new ModuleEntity { Id = Guid.NewGuid(), Name = name, SortOrder = index + 1 }));
        db.CustomFields.AddRange(
            new CustomFieldEntity { Id = "cf-risk", Name = "风险等级", Type = "single", OptionsJson = JsonSerializer.Serialize(new[] { "高", "中", "低" }), SortOrder = 1 },
            new CustomFieldEntity { Id = "cf-build", Name = "目标构建版本", Type = "text", SortOrder = 2 },
            new CustomFieldEntity { Id = "cf-reviewer", Name = "验收人", Type = "person", SortOrder = 3 });

        db.Iterations.AddRange(
            new IterationEntity { Id = "it-previous", Name = "20260817-20260821", StartDate = new DateOnly(2026, 8, 17), EndDate = new DateOnly(2026, 8, 21), State = "completed", Goal = "完成稳定性修复与体验收尾", CreatedAt = now },
            new IterationEntity { Id = "it-current", Name = "20260824-20260828", StartDate = new DateOnly(2026, 8, 24), EndDate = new DateOnly(2026, 8, 28), State = "active", Goal = "集中优化性能、文档与设置界面", CreatedAt = now },
            new IterationEntity { Id = "it-next", Name = "20260831-20260904", StartDate = new DateOnly(2026, 8, 31), EndDate = new DateOnly(2026, 9, 4), State = "upcoming", Goal = "内容整合与验收", CreatedAt = now });

        db.Requirements.AddRange(
            Requirement("REQ-0048", "列表加载性能优化", "性能", "urgent", "in_progress", "u-liu", "u-admin", "it-current", null, new DateOnly(2026, 8, 27), "优化列表加载和翻页响应。", now.AddDays(-6), JsonSerializer.Serialize(new Dictionary<string, object?> { ["cf-risk"] = "高", ["cf-build"] = "0.9.42" })),
            Requirement("REQ-0047", "帮助文档结构调整", "文档", "high", "review", "u-chen", "u-admin", "it-current", null, new DateOnly(2026, 8, 26), "调整帮助文档章节，补充使用说明。", now.AddDays(-5), JsonSerializer.Serialize(new Dictionary<string, object?> { ["cf-risk"] = "中" })),
            Requirement("REQ-0045", "设置类页面走查", "UI", "medium", "in_progress", "u-wang", "u-admin", "it-current", null, new DateOnly(2026, 8, 28), "统一设置类页面的交互、文本和异常状态。", now.AddDays(-8), "{}"),
            Requirement("REQ-0041", "成员管理页面走查", "UI", "medium", "todo", "u-wang", "u-admin", "it-current", "REQ-0045", new DateOnly(2026, 8, 28), "检查成员管理页面状态。", now.AddDays(-7), "{}"));
        db.History.AddRange(
            History("REQ-0048", "u-liu", "状态变更", "未开始 → 进行中", now.AddDays(-1)),
            History("REQ-0047", "u-chen", "状态变更", "进行中 → 待验收", now.AddHours(-8)));
        db.Comments.Add(new CommentEntity { Id = Guid.NewGuid(), RequirementId = "REQ-0048", AuthorId = "u-wang", Content = "@示例成员甲 今天请优先验证列表加载。", CreatedAt = now.AddHours(-5) });
        await db.SaveChangesAsync(cancellationToken);
        }
        await EnsureFeatureSeedsAsync(cancellationToken);
    }

    private async Task UpgradeSqliteSchemaAsync(CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlite()) return;
        var connection=db.Database.GetDbConnection(); if(connection.State!=System.Data.ConnectionState.Open)await connection.OpenAsync(cancellationToken);
        var userColumns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);await using(var userCommand=connection.CreateCommand()){userCommand.CommandText="PRAGMA table_info('Users')";await using var userReader=await userCommand.ExecuteReaderAsync(cancellationToken);while(await userReader.ReadAsync(cancellationToken))userColumns.Add(userReader.GetString(1));}
        if(!userColumns.Contains("WeComEmail"))await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN "WeComEmail" TEXT NULL""",cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_WeComUserId" ON "Users" ("WeComUserId")""",cancellationToken);
        var columns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);await using(var command=connection.CreateCommand()){command.CommandText="PRAGMA table_info('Requirements')";await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))columns.Add(reader.GetString(1));}
        if(!columns.Contains("AssigneeIdsJson"))await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Requirements" ADD COLUMN "AssigneeIdsJson" TEXT NULL""",cancellationToken);
        if(!columns.Contains("ReviewerId"))await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Requirements" ADD COLUMN "ReviewerId" TEXT NULL""",cancellationToken);
        if(!columns.Contains("RequirementTypeId"))await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Requirements" ADD COLUMN "RequirementTypeId" TEXT NULL""",cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "RequirementTypes" ("Id" TEXT NOT NULL CONSTRAINT "PK_RequirementTypes" PRIMARY KEY, "Name" TEXT NOT NULL, "Color" TEXT NOT NULL, "SortOrder" INTEGER NOT NULL, "Enabled" INTEGER NOT NULL)""",cancellationToken);
        var typeColumns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);await using(var typeCommand=connection.CreateCommand()){typeCommand.CommandText="PRAGMA table_info('RequirementTypes')";await using var typeReader=await typeCommand.ExecuteReaderAsync(cancellationToken);while(await typeReader.ReadAsync(cancellationToken))typeColumns.Add(typeReader.GetString(1));}
        if(!typeColumns.Contains("Color"))await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "RequirementTypes" ADD COLUMN "Color" TEXT NOT NULL DEFAULT '#0A84FF'""",cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_RequirementTypes_Name" ON "RequirementTypes" ("Name")""",cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "RequirementDefaults" ("Id" INTEGER NOT NULL CONSTRAINT "PK_RequirementDefaults" PRIMARY KEY, "Module" TEXT NULL, "Priority" TEXT NOT NULL, "StatusId" TEXT NULL, "AssigneeId" TEXT NULL, "ReviewerId" TEXT NULL, "RequirementTypeId" TEXT NULL, "IterationMode" TEXT NOT NULL, "IterationId" TEXT NULL, "DueDateOffsetDays" INTEGER NULL, "DescriptionTemplate" TEXT NOT NULL)""",cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "SystemBranding" ("Id" INTEGER NOT NULL CONSTRAINT "PK_SystemBranding" PRIMARY KEY, "ProjectName" TEXT NOT NULL, "LoginBackgroundUrl" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL)""",cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "JobLocks" ("Key" TEXT NOT NULL CONSTRAINT "PK_JobLocks" PRIMARY KEY, "Owner" TEXT NOT NULL, "ExpiresAt" TEXT NOT NULL)""",cancellationToken);
    }

    private async Task EnsureFeatureSeedsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await db.Users.AnyAsync(x => x.Id == SystemUsers.DeletedId, cancellationToken))
        {
            var deletedUser = NewUser(SystemUsers.DeletedId, "__deleted__", "已删除用户", Roles.Developer, "删", "#71717a", now);
            deletedUser.IsActive = false;
            deletedUser.PasswordHash = passwordService.Hash(deletedUser, Guid.NewGuid().ToString("N"));
            db.Users.Add(deletedUser);
        }
        var typeSeeds = new[]
        {
            new RequirementTypeEntity { Id = Guid.Parse("11111111-1111-1111-1111-111111111101"), Name = "周版本反馈", Color = "#0A84FF", SortOrder = 1 },
            new RequirementTypeEntity { Id = Guid.Parse("11111111-1111-1111-1111-111111111102"), Name = "周版本预期内容", Color = "#BF5AF2", SortOrder = 2 },
            new RequirementTypeEntity { Id = Guid.Parse("11111111-1111-1111-1111-111111111103"), Name = "周开发新增内容", Color = "#FF9F0A", SortOrder = 3 }
        };
        foreach (var seed in typeSeeds)
        {
            var existing=await db.RequirementTypes.SingleOrDefaultAsync(x=>x.Id==seed.Id,cancellationToken);
            if(existing is null)db.RequirementTypes.Add(seed);else if(existing.Color!=seed.Color)existing.Color=seed.Color;
        }

        if (!await db.SystemBranding.AnyAsync(cancellationToken))
            db.SystemBranding.Add(new SystemBrandingEntity { Id = 1, ProjectName = "G43", LoginBackgroundUrl = "/login-background.svg", UpdatedAt = now });
        if (!await db.RequirementDefaults.AnyAsync(cancellationToken))
            db.RequirementDefaults.Add(new RequirementDefaultsEntity { Id = 1, Module = "UI", Priority = "medium", StatusId = "todo", RequirementTypeId = typeSeeds[0].Id, IterationMode = "current" });

        var holidays = new Dictionary<DateOnly, string>();
        AddRange(holidays, new DateOnly(2026,1,1), new DateOnly(2026,1,3), "元旦假期");
        AddRange(holidays, new DateOnly(2026,2,15), new DateOnly(2026,2,23), "春节假期");
        AddRange(holidays, new DateOnly(2026,4,4), new DateOnly(2026,4,6), "清明节假期");
        AddRange(holidays, new DateOnly(2026,5,1), new DateOnly(2026,5,5), "劳动节假期");
        AddRange(holidays, new DateOnly(2026,6,19), new DateOnly(2026,6,21), "端午节假期");
        AddRange(holidays, new DateOnly(2026,9,25), new DateOnly(2026,9,27), "中秋节假期");
        AddRange(holidays, new DateOnly(2026,10,1), new DateOnly(2026,10,7), "国庆节假期");
        var workdays = new Dictionary<DateOnly,string>
        {
            [new DateOnly(2026,1,4)]="元旦调休上班", [new DateOnly(2026,2,14)]="春节调休上班", [new DateOnly(2026,2,28)]="春节调休上班",
            [new DateOnly(2026,5,9)]="劳动节调休上班", [new DateOnly(2026,9,20)]="国庆节调休上班", [new DateOnly(2026,10,10)]="国庆节调休上班"
        };
        foreach (var item in holidays)
            if (!await db.WorkCalendar.AnyAsync(x => x.Date == item.Key, cancellationToken)) db.WorkCalendar.Add(new WorkCalendarEntity { Date=item.Key, IsWorkday=false, Note=item.Value });
        foreach (var item in workdays)
            if (!await db.WorkCalendar.AnyAsync(x => x.Date == item.Key, cancellationToken)) db.WorkCalendar.Add(new WorkCalendarEntity { Date=item.Key, IsWorkday=true, Note=item.Value });

        await db.SaveChangesAsync(cancellationToken);
        var userIds = await db.Users.Select(x => x.Id).ToListAsync(cancellationToken);
        foreach (var requirement in await db.Requirements.Where(x => x.ReviewerId == null).ToListAsync(cancellationToken))
        {
            try
            {
                var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(requirement.CustomValuesJson);
                if (json is not null && json.TryGetValue("cf-reviewer", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var reviewer = value.GetString(); if (reviewer is not null && userIds.Contains(reviewer)) requirement.ReviewerId = reviewer;
                }
            }
            catch { }
            requirement.RequirementTypeId ??= typeSeeds[0].Id;
        }
        foreach (var field in await db.CustomFields.Where(x => x.Enabled).ToListAsync(cancellationToken)) field.Enabled = false;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddRange(Dictionary<DateOnly,string> target, DateOnly start, DateOnly end, string note)
    {
        for (var date=start; date<=end; date=date.AddDays(1)) target[date]=note;
    }

    private static UserEntity NewUser(string id, string account, string name, string role, string initials, string color, DateTimeOffset now) => new()
    { Id = id, Account = account, Name = name, Role = role, Initials = initials, Color = color, PasswordHash = string.Empty, IsActive = true, CreatedAt = now, UpdatedAt = now };
    private static RequirementEntity Requirement(string id, string title, string module, string priority, string status, string? assignee, string creator, string? iteration, string? parent, DateOnly? due, string description, DateTimeOffset created, string customValues) => new()
    { Id = id, Title = title, Module = module, Priority = priority, StatusId = status, AssigneeId = assignee, CreatorId = creator, IterationId = iteration, ParentId = parent, DueDate = due, Description = description, CreatedAt = created, UpdatedAt = created, CustomValuesJson = customValues };
    private static HistoryEntity History(string requirement, string actor, string action, string detail, DateTimeOffset at) => new() { Id = Guid.NewGuid(), RequirementId = requirement, ActorId = actor, Action = action, Detail = detail, CreatedAt = at };
}
