using System.Security.Claims;
using System.Text.Json;
using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Ground43.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Endpoints;

public static class RequirementEndpoints
{
    private static readonly string[] Priorities = ["urgent", "high", "medium", "low"];

    public static IEndpointRouteBuilder MapRequirementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/requirements").RequireAuthorization().WithTags("Requirements");
        group.MapGet("/", ListAsync);
        group.MapGet("/tree", TreeAsync);
        group.MapGet("/{id}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPost("/batch", BatchCreateAsync);
        group.MapPost("/family", CreateFamilyAsync);
        group.MapPost("/{id}/children", LinkChildrenAsync);
        group.MapPatch("/{id}", UpdateAsync);
        group.MapDelete("/{id}", DeleteAsync).RequireAuthorization("Admin");
        group.MapPost("/{id}/comments", AddCommentAsync);
        group.MapDelete("/{id}/comments/{commentId:guid}", DeleteCommentAsync);
        return app;
    }

    private static IQueryable<RequirementEntity> DetailQuery(AppDbContext db) => db.Requirements.Include(x => x.Comments).Include(x => x.History).Include(x => x.Attachments).AsSplitQuery();

    private static async Task<IResult> ListAsync(string? query, string? statusId, string? assigneeId, string? iterationId, string? priority, AppDbContext db, CancellationToken ct)
    {
        var items = DetailQuery(db).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLower();
            items = items.Where(x => x.Id.ToLower().Contains(term) || x.Title.ToLower().Contains(term) || x.Module.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(statusId)) items = items.Where(x => x.StatusId == statusId);
        if (!string.IsNullOrWhiteSpace(assigneeId)) items = items.AssignedTo(assigneeId);
        if (!string.IsNullOrWhiteSpace(iterationId)) items = items.Where(x => x.IterationId == iterationId);
        if (!string.IsNullOrWhiteSpace(priority)) items = items.Where(x => x.Priority == priority);
        return Results.Ok((await items.ToListAsync(ct)).OrderByDescending(x => x.UpdatedAt).Select(Mapping.ToDto));
    }

    private static async Task<IResult> TreeAsync(string? query, string? statusId, string? assigneeId, string? iterationId, string? priority, string? requirementTypeId, HttpContext context, bool mine = false, int page = 1, int pageSize = 20, string sort = "updated", AppDbContext db = null!, CancellationToken ct = default)
    {
        page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,100);
        IQueryable<RequirementEntity> filtered=db.Requirements.AsNoTracking();
        if (mine) { var userId = context.User.UserId(); var assigned = db.Requirements.AssignedTo(userId).Select(x => x.Id); filtered = filtered.Where(x => assigned.Contains(x.Id) || (x.ReviewerId == userId && x.StatusId == "review")); }
        if(!string.IsNullOrWhiteSpace(query)){var term=query.Trim().ToLower();filtered=filtered.Where(x=>x.Id.ToLower().Contains(term)||x.Title.ToLower().Contains(term)||x.Module.ToLower().Contains(term));}
        if(!string.IsNullOrWhiteSpace(statusId)){var values=statusId.Split(',',StringSplitOptions.RemoveEmptyEntries);filtered=filtered.Where(x=>values.Contains(x.StatusId!));}
        if(!string.IsNullOrWhiteSpace(assigneeId)){var values=assigneeId.Split(',',StringSplitOptions.RemoveEmptyEntries); var matches = db.Requirements.Where(x => false); foreach(var value in values) matches = matches.Union(db.Requirements.AssignedTo(value)); var ids = matches.Select(x => x.Id); filtered=filtered.Where(x=>ids.Contains(x.Id));}
        if(!string.IsNullOrWhiteSpace(iterationId)){var values=iterationId.Split(',',StringSplitOptions.RemoveEmptyEntries);filtered=filtered.Where(x=>values.Contains(x.IterationId!));}
        if(!string.IsNullOrWhiteSpace(priority)){var values=priority.Split(',',StringSplitOptions.RemoveEmptyEntries);filtered=filtered.Where(x=>values.Contains(x.Priority!));}
        if(!string.IsNullOrWhiteSpace(requirementTypeId)) {
            var parts=requirementTypeId.Split(',',StringSplitOptions.RemoveEmptyEntries);
            if(parts.Any(x=>!Guid.TryParse(x,out _))) return Results.BadRequest(new { message="需求类型筛选无效" });
            var values=parts.Select(Guid.Parse).ToArray();
            filtered=filtered.Where(x=>x.RequirementTypeId.HasValue && values.Contains(x.RequirementTypeId.Value));
        }
        var matchedIds=filtered.Select(x=>x.Id);var rootIds=filtered.Select(x=>x.ParentId??x.Id).Distinct();var rootCount=await rootIds.CountAsync(ct);var requirementCount=await filtered.CountAsync(ct);
        IQueryable<RequirementEntity> roots=db.Requirements.AsNoTracking().Where(x=>rootIds.Contains(x.Id));
        List<string> pageRootIds;
        if (sort == "status")
        {
            var rootList = await roots.ToListAsync(ct);
            pageRootIds = rootList.OrderBy(x => RequirementSort.StatusRank(x.StatusId))
                .ThenBy(x => RequirementSort.PriorityRank(x.Priority)).ThenBy(x => RequirementSort.Number(x.Id))
                .ThenBy(x => x.Id, StringComparer.Ordinal).Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToList();
        }
        else if(db.Database.IsSqlite())
        {
            var rootList=await roots.ToListAsync(ct);
            var userNames=await db.Users.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.Name,ct);
            rootList=sort switch {"priority"=>rootList.OrderBy(x=>x.Priority=="urgent"?0:x.Priority=="high"?1:x.Priority=="medium"?2:3).ThenByDescending(x=>x.UpdatedAt).ToList(),"assignee"=>rootList.OrderBy(x=>x.AssigneeId==null).ThenBy(x=>x.AssigneeId==null ? "" : userNames.GetValueOrDefault(x.AssigneeId, ""), StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), false)).ThenByDescending(x=>x.UpdatedAt).ToList(),"due"=>rootList.OrderBy(x=>x.DueDate==null).ThenBy(x=>x.DueDate).ThenByDescending(x=>x.UpdatedAt).ToList(),_=>rootList.OrderByDescending(x=>x.UpdatedAt).ToList()};
            pageRootIds=rootList.Skip((page-1)*pageSize).Take(pageSize).Select(x=>x.Id).ToList();
        }
        else
        {
            roots=sort switch {"priority"=>roots.OrderBy(x=>x.Priority=="urgent"?0:x.Priority=="high"?1:x.Priority=="medium"?2:3).ThenByDescending(x=>x.UpdatedAt),"assignee"=>roots.OrderBy(x=>x.AssigneeId==null).ThenBy(x=>db.Users.Where(u=>u.Id==x.AssigneeId).Select(u=>u.Name).FirstOrDefault()).ThenByDescending(x=>x.UpdatedAt),"due"=>roots.OrderBy(x=>x.DueDate==null).ThenBy(x=>x.DueDate).ThenByDescending(x=>x.UpdatedAt),_=>roots.OrderByDescending(x=>x.UpdatedAt)};
            pageRootIds=await roots.Skip((page-1)*pageSize).Take(pageSize).Select(x=>x.Id).ToListAsync(ct);
        }
        var entities=await DetailQuery(db).AsNoTracking().Where(x=>pageRootIds.Contains(x.Id)||(x.ParentId!=null&&pageRootIds.Contains(x.ParentId))).ToListAsync(ct);
        var entityMap=entities.ToDictionary(x=>x.Id);var matchedSet=(await matchedIds.ToListAsync(ct)).ToHashSet();
        var groups=new List<RequirementTreeGroupDto>();foreach(var rootId in pageRootIds){if(!entityMap.TryGetValue(rootId,out var root))continue;var children=entities.Where(x=>x.ParentId==rootId&&matchedSet.Contains(x.Id)).OrderByDescending(x=>x.Id).Select(Mapping.ToDto).ToArray();groups.Add(new RequirementTreeGroupDto(root.ToDto(),children,matchedSet.Contains(rootId)));}
        return Results.Ok(new RequirementTreePageDto(page,pageSize,rootCount,requirementCount,groups));
    }

    private static async Task<IResult> GetAsync(string id, AppDbContext db, CancellationToken ct)
    {
        var entity = await DetailQuery(db).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? Results.NotFound() : Results.Ok(entity.ToDto());
    }

    private static async Task<IResult> LinkChildrenAsync(string id, LinkChildrenRequest request, HttpContext context, AppDbContext db, CancellationToken ct)
    {
        if (request.ChildIds is null || request.ChildIds.Length is < 1 or > 100) return Results.BadRequest(new { message = "请选择 1～100 个子需求" });
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var parent = await db.Requirements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (parent is null || parent.ParentId != null) return Results.BadRequest(new { message = "只能为顶层需求绑定子需求" });
        var ids = request.ChildIds.Distinct().ToArray();
        var children = await db.Requirements.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (ids.Contains(id) || children.Count != ids.Length || children.Any(x => x.ParentId != null && x.ParentId != id)
            || await db.Requirements.AnyAsync(x => x.ParentId != null && ids.Contains(x.ParentId), ct))
            return Results.Conflict(new { message = "选中的需求已有父需求、包含子需求或不存在，本次未绑定" });
        if (parent.StatusId is "completed" or "closed" && children.Any(x => x.StatusId is not ("completed" or "closed")))
            return Results.BadRequest(new { message = "已完成的父需求不能绑定未完成子需求" });
        foreach (var child in children.Where(x => x.ParentId != id))
        {
            var now = DateTimeOffset.UtcNow;
            var changed = await db.Requirements.Where(x => x.Id == child.Id && x.ParentId == null && x.Version == child.Version)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, id).SetProperty(x => x.Version, x => x.Version + 1).SetProperty(x => x.UpdatedAt, now), ct);
            if (changed != 1) return Results.Conflict(new { message = "子需求已被其他人修改，本次未绑定，请重试" });
            db.History.Add(new HistoryEntity { Id = Guid.NewGuid(), RequirementId = child.Id, ActorId = context.User.UserId(), Action = "需求更新", Detail = $"绑定父需求：{id}", CreatedAt = now });
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> CreateFamilyAsync(CreateFamilyRequest request, HttpContext context, AppDbContext db, RequirementCodeService codes, ReviewNotificationService notifications, AssignmentNotificationService assignments, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.Parent is null || request.Children is null || request.ExistingChildIds is null
            || request.Children.Length + request.ExistingChildIds.Length > 100)
            return Results.BadRequest(new { message = "最多支持 100 个子需求，需要有效提交标识" });
        var hasChildren = request.Children.Length + request.ExistingChildIds.Length > 0;
        if (hasChildren && request.Parent.ParentId is not null)
            return Results.BadRequest(new { message = "创建子需求时不能同时指定父需求，不支持嵌套创建孙需求" });
        var hash = "family:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(request))));
        var userId = context.User.UserId();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var previous = await db.BatchCreations.FindAsync([userId, request.RequestId], ct);
        if (previous is not null)
        {
            if (previous.PayloadHash != hash) return Results.Conflict(new { message = "提交标识已用于其他内容，请检查上次创建结果" });
            var savedIds = System.Text.Json.JsonSerializer.Deserialize<string[]>(previous.ResultJson)!;
            return Results.Ok(new { parentId = savedIds[0], requirements = (await DetailQuery(db).AsNoTracking().Where(x => savedIds.Contains(x.Id)).ToListAsync(ct)).Select(Mapping.ToDto) });
        }
        var existingIds = request.ExistingChildIds.Distinct().ToArray();
        var existing = await db.Requirements.Where(x => existingIds.Contains(x.Id)).ToListAsync(ct);
        if (existing.Count != existingIds.Length || existing.Any(x => x.ParentId != null)
            || await db.Requirements.AnyAsync(x => x.ParentId != null && existingIds.Contains(x.ParentId), ct))
            return Results.Conflict(new { message = "选中的需求不存在、已有父需求或包含子需求，请重新选择。本次未创建任何需求。" });
        var all = new[] { request.Parent }.Concat(request.Children).ToArray();
        for (var i = 0; i < all.Length; i++)
        {
            var item = all[i];
            if (item is null || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 500 || string.IsNullOrWhiteSpace(item.Description)
                || (i > 0 && item.ParentId != null) || !await db.RequirementTypes.AnyAsync(x => x.Id == item.RequirementTypeId && x.Enabled, ct)
                || (item.ReviewerId != null && !await db.Users.AnyAsync(x => x.Id == item.ReviewerId && x.IsActive, ct)))
                return Results.BadRequest(new { message = i == 0 ? "请检查父需求标题、描述、类型和验收人" : $"第 {i} 个子需求：请检查标题、描述、类型和验收人" });
        }
        if (hasChildren && (request.Parent.StatusId is "completed" or "closed")
            && (request.Children.Any(x => x.StatusId is not ("completed" or "closed")) || existing.Any(x => x.StatusId is not ("completed" or "closed"))))
            return Results.BadRequest(new { message = "仍有未完成子需求，父需求不能直接设置为验收完成或已关闭" });
        var receipt = new BatchCreationEntity { UserId = userId, RequestId = request.RequestId, PayloadHash = hash };
        db.BatchCreations.Add(receipt);
        await db.SaveChangesAsync(ct);
        var ids = new List<string>();
        for (var i = 0; i < all.Length; i++)
        {
            var item = i == 0 ? all[i] : all[i] with { ParentId = ids[0] };
            var result = await CreateAsync(item, context, db, codes, notifications, assignments, ct, false);
            if (result is not IStatusCodeHttpResult status || status.StatusCode != 201)
                return Results.BadRequest(new { message = i == 0 ? "父需求校验失败，本次未创建" : $"第 {i} 个子需求校验失败，本次未创建", detail = (result as IValueHttpResult)?.Value });
            ids.Add(((RequirementDto)((IValueHttpResult)result).Value!).Id);
        }
        foreach (var child in existing)
        {
            var now = DateTimeOffset.UtcNow;
            var claimed = await db.Requirements.Where(x => x.Id == child.Id && x.ParentId == null && x.Version == child.Version)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, ids[0]).SetProperty(x => x.Version, x => x.Version + 1).SetProperty(x => x.UpdatedAt, now), ct);
            if (claimed != 1) return Results.Conflict(new { message = "子需求已被其他人修改，请重新选择。本次未创建。" });
            db.History.Add(new HistoryEntity { Id = Guid.NewGuid(), RequirementId = child.Id, ActorId = userId, Action = "需求更新", Detail = $"绑定父需求：{ids[0]}", CreatedAt = now });
            ids.Add(child.Id);
        }
        receipt.ResultJson = System.Text.Json.JsonSerializer.Serialize(ids);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { parentId = ids[0], requirements = (await DetailQuery(db).AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct)).Select(Mapping.ToDto) });
    }

    private static async Task<IResult> BatchCreateAsync(BatchCreateRequest request, HttpContext context, AppDbContext db, RequirementCodeService codes, ReviewNotificationService notifications, AssignmentNotificationService assignments, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.Items is null || request.Items.Length is < 1 or > 100)
            return Results.BadRequest(new { message = "每批支持 1～100 条需求，需要有效的批次标识" });
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(request.Items))));
        var userId = context.User.UserId();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var previous = await db.BatchCreations.FindAsync([userId, request.RequestId], ct);
        if (previous is not null) return previous.PayloadHash != hash
            ? Results.Conflict(new { message = "批次标识已用于其他内容，请检查上次创建结果" })
            : Results.Ok(new { ids = System.Text.Json.JsonSerializer.Deserialize<string[]>(previous.ResultJson) });
        var receipt = new BatchCreationEntity { UserId = userId, RequestId = request.RequestId, PayloadHash = hash };
        db.BatchCreations.Add(receipt);
        await db.SaveChangesAsync(ct);
        var ids = new List<string>();
        for (var index = 0; index < request.Items.Length; index++)
        {
            var item = request.Items[index];
            if (item is null || item.ParentId is not null || item.Title?.Length > 500 || item.RequirementTypeId is null)
                return Results.BadRequest(new { message = $"第 {index + 1} 行：需要有效标题和需求类型，不支持父子关系" });
            if (!await db.RequirementTypes.AnyAsync(x => x.Id == item.RequirementTypeId && x.Enabled, ct) || (item.ReviewerId is not null && !await db.Users.AnyAsync(x => x.Id == item.ReviewerId && x.IsActive, ct)))
                return Results.BadRequest(new { message = $"第 {index + 1} 行：需求类型或验收人不存在、已停用" });
            var result = await CreateAsync(item, context, db, codes, notifications, assignments, ct);
            if (result is not IStatusCodeHttpResult status || status.StatusCode != 201)
                return Results.BadRequest(new { message = $"第 {index + 1} 行校验失败，本批未创建，请检查字段和权限", detail = (result as IValueHttpResult)?.Value });
            ids.Add(((RequirementDto)((IValueHttpResult)result).Value!).Id);
        }
        receipt.ResultJson = System.Text.Json.JsonSerializer.Serialize(ids);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { ids });
    }

    private static async Task<IResult> CreateAsync(CreateRequirementRequest request, HttpContext context, AppDbContext db, RequirementCodeService codes, ReviewNotificationService notifications, AssignmentNotificationService assignments, CancellationToken ct, bool useDefaults = true)
    {
        var defaults=await db.RequirementDefaults.AsNoTracking().SingleAsync(x=>x.Id==1,ct);
        var module=string.IsNullOrWhiteSpace(request.Module)?defaults.Module??"UI":request.Module;
        var priority=string.IsNullOrWhiteSpace(request.Priority)?defaults.Priority:request.Priority;
        var statusId=string.IsNullOrWhiteSpace(request.StatusId)?defaults.StatusId??"todo":request.StatusId;
        var assigneeId=request.AssigneeId??(useDefaults?defaults.AssigneeId:null);var reviewerId=request.ReviewerId??(useDefaults?defaults.ReviewerId:null);var typeId=request.RequirementTypeId??defaults.RequirementTypeId;
        var assigneeIds = request.AssigneeIds ?? (assigneeId is null ? [] : new[] { assigneeId });
        assigneeIds = assigneeIds.Distinct().ToArray();
        if (assigneeIds.Any(string.IsNullOrWhiteSpace) || await db.Users.CountAsync(x => assigneeIds.Contains(x.Id) && x.IsActive, ct) != assigneeIds.Length)
            return Results.BadRequest(new { message = "处理人不存在或已停用" });
        assigneeId = assigneeIds.FirstOrDefault();
        var iterationId=request.IterationId;if(useDefaults&&iterationId is null&&defaults.IterationMode=="current")iterationId=await db.Iterations.Where(x=>x.State=="active").OrderByDescending(x=>x.StartDate).Select(x=>x.Id).FirstOrDefaultAsync(ct);else if(useDefaults&&iterationId is null&&defaults.IterationMode=="specific")iterationId=defaults.IterationId;
        var validation = await ValidateAsync(null, module, priority, statusId, assigneeId, iterationId, request.ParentId, reviewerId, typeId, context, db, ct);
        if (validation is not null) return validation;
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description)) return Results.BadRequest(new { message = "需求标题和描述不能为空" });
        if (!TryDate(request.DueDate, out var dueDate)) return Results.BadRequest(new { message = "期望完成日期格式无效" });
        if(useDefaults&&dueDate is null&&defaults.DueDateOffsetDays.HasValue)dueDate=DateOnly.FromDateTime(DateTime.Today).AddDays(defaults.DueDateOffsetDays.Value);
        var now = DateTimeOffset.UtcNow; var id = await codes.NextAsync(ct); var userId = context.User.UserId();
        var entity = new RequirementEntity
        {
            Id = id, Title = request.Title.Trim(), Module = module.Trim(), Priority = priority,
            StatusId = statusId, AssigneeId = assigneeId, CreatorId = userId,
            IterationId = iterationId, ParentId = request.ParentId, ReviewerId=reviewerId, RequirementTypeId=typeId, DueDate = dueDate,
            Description = string.IsNullOrWhiteSpace(request.Description)?defaults.DescriptionTemplate:request.Description.Trim(), CustomValuesJson = "{}", CreatedAt = now, UpdatedAt = now
        };
        entity.SetAssigneeIds(assigneeIds);
        entity.History.Add(new HistoryEntity { Id = Guid.NewGuid(), RequirementId = id, ActorId = userId, Action = "创建需求", Detail = $"创建了 {id}", CreatedAt = now });
        db.Requirements.Add(entity);
        await notifications.RecordChangeAsync(entity, null, null, ct);
        await assignments.RecordChangeAsync(entity, null, userId, true, ct);
        await db.SaveChangesAsync(ct);
        var saved = await DetailQuery(db).AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return Results.Created($"/api/requirements/{id}", saved.ToDto());
    }

    private static async Task<IResult> UpdateAsync(string id, UpdateRequirementRequest request, HttpContext context, AppDbContext db, ReviewNotificationService notifications, AssignmentNotificationService assignments, CancellationToken ct)
    {
        var entity = await db.Requirements.Include(x => x.Children).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Results.NotFound();
        if (request.Version.HasValue && request.Version.Value != entity.Version) return Results.Conflict(new { message = "需求已被其他用户修改", currentVersion = entity.Version });
        var previousStatus = entity.StatusId;
        var previousReviewer = entity.ReviewerId;
        var previousAssignees = entity.GetAssigneeIds();
        var module = request.Module ?? entity.Module; var priority = request.Priority ?? entity.Priority; var status = request.StatusId ?? entity.StatusId;
        var assignee = request.ClearAssignee ? null : request.AssigneeId ?? entity.AssigneeId;
        var assigneeIds = request.AssigneeIds ?? (request.ClearAssignee ? [] : request.AssigneeId is not null ? new[] { request.AssigneeId } : previousAssignees);
        assigneeIds = assigneeIds.Distinct().ToArray();
        if (assigneeIds.Any(string.IsNullOrWhiteSpace) || await db.Users.CountAsync(x => assigneeIds.Contains(x.Id) && x.IsActive, ct) != assigneeIds.Length)
            return Results.BadRequest(new { message = "处理人不存在或已停用" });
        assignee = assigneeIds.FirstOrDefault();
        var iteration = request.ClearIteration ? null : request.IterationId ?? entity.IterationId;
        var parent = request.ClearParent ? null : request.ParentId ?? entity.ParentId;
        var reviewer=request.ClearReviewer?null:request.ReviewerId??entity.ReviewerId;var typeId=request.ClearRequirementType?null:request.RequirementTypeId??entity.RequirementTypeId;
        var validation = await ValidateAsync(entity, module, priority, status, assignee, iteration, parent, reviewer, typeId, context, db, ct);
        if (validation is not null) return validation;
        if (request.Title is not null && string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest(new { message = "需求标题不能为空" });
        if (request.Description is not null && string.IsNullOrWhiteSpace(request.Description)) return Results.BadRequest(new { message = "需求描述不能为空" });
        DateOnly? due = entity.DueDate;
        if (request.ClearDueDate) due = null;
        else if (request.DueDate is not null && !TryDate(request.DueDate, out due)) return Results.BadRequest(new { message = "期望完成日期格式无效" });

        var changes = new List<string>();
        var title = request.Title?.Trim(); if (title is not null && entity.Title != title) { changes.Add($"标题：{entity.Title} → {title}"); entity.Title = title; }
        var requestedModule = request.Module?.Trim(); if (requestedModule is not null && entity.Module != requestedModule) { changes.Add($"模块：{entity.Module} → {requestedModule}"); entity.Module = requestedModule; }
        if (request.Priority is not null && entity.Priority != request.Priority) { changes.Add($"优先级：{entity.Priority} → {request.Priority}"); entity.Priority = request.Priority; }
        if (request.StatusId is not null && entity.StatusId != request.StatusId) { changes.Add($"状态：{entity.StatusId} → {request.StatusId}"); entity.StatusId = request.StatusId; }
        if (!previousAssignees.SequenceEqual(assigneeIds)) { changes.Add($"处理人：{string.Join(";", previousAssignees)} → {string.Join(";", assigneeIds)}"); entity.SetAssigneeIds(assigneeIds); }
        if (entity.IterationId != iteration) { changes.Add($"迭代：{entity.IterationId ?? "空"} → {iteration ?? "空"}"); entity.IterationId = iteration; }
        if (entity.ParentId != parent) { changes.Add($"父需求：{entity.ParentId ?? "空"} → {parent ?? "空"}"); entity.ParentId = parent; }
        if(entity.ReviewerId!=reviewer){changes.Add($"验收人：{entity.ReviewerId??"空"} → {reviewer??"空"}");entity.ReviewerId=reviewer;}
        if(entity.RequirementTypeId!=typeId){changes.Add("更新了需求单类型");entity.RequirementTypeId=typeId;}
        if (entity.DueDate != due) { changes.Add($"期望完成时间：{entity.DueDate} → {due}"); entity.DueDate = due; }
        var description = request.Description?.Trim(); if (description is not null && entity.Description != description) { changes.Add("更新了描述"); entity.Description = description; }
        if (request.CustomValues.HasValue)
        {
            var json = NormalizeJson(request.CustomValues);
            if (entity.CustomValuesJson != json) { entity.CustomValuesJson = json; changes.Add("更新了自定义字段"); }
        }
        if (changes.Count == 0) return Results.Ok((await DetailQuery(db).AsNoTracking().SingleAsync(x => x.Id == id, ct)).ToDto());
        entity.UpdatedAt = DateTimeOffset.UtcNow; entity.Version++;
        db.History.Add(new HistoryEntity { Id = Guid.NewGuid(), RequirementId = id, ActorId = context.User.UserId(), Action = "需求更新", Detail = request.Summary?.Trim() ?? string.Join("；", changes), CreatedAt = entity.UpdatedAt });
        await notifications.RecordChangeAsync(entity, previousStatus, previousReviewer, ct);
        await assignments.RecordAssigneesChangeAsync(entity, previousAssignees, context.User.UserId(), false, ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { message = "需求已被其他用户修改" }); }
        var saved = await DetailQuery(db).AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return Results.Ok(saved.ToDto());
    }

    private static async Task<IResult> DeleteAsync(string id, HttpContext context, AppDbContext db, AttachmentStorage storage, CancellationToken ct)
    {
        var entity = await db.Requirements.Include(x => x.Children).Include(x => x.Attachments).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Results.NotFound();
        foreach (var child in entity.Children)
        {
            child.ParentId = null; child.Version++; child.UpdatedAt = DateTimeOffset.UtcNow;
            db.History.Add(new HistoryEntity { Id = Guid.NewGuid(), RequirementId = child.Id, ActorId = context.User.UserId(), Action = "父子关系变更", Detail = $"父需求 {id} 已删除，当前需求转为顶层需求", CreatedAt = child.UpdatedAt });
        }
        var paths = entity.Attachments.Select(x => x.RelativePath).ToArray();
        db.Requirements.Remove(entity); await db.SaveChangesAsync(ct);
        foreach (var path in paths) storage.DeleteFile(path);
        return Results.NoContent();
    }

    private static Task<IResult> AddCommentAsync(string id, CreateCommentRequest request, HttpContext context, CommentService comments, CancellationToken ct)
        => comments.CreateAsync(id, request, context.User.UserId(), ct);

    private static async Task<IResult> DeleteCommentAsync(string id, Guid commentId, HttpContext context, AppDbContext db, AttachmentStorage storage, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var comment = await db.Comments.SingleOrDefaultAsync(x => x.Id == commentId && x.RequirementId == id, ct);
        if (comment is null) return Results.NotFound();
        if (comment.AuthorId != context.User.UserId() && !context.User.IsInRole(Roles.Admin)) return Results.Forbid();
        var attachments = await db.Attachments.Where(x => x.CommentId == commentId).ToListAsync(ct);
        var requirement = await db.Requirements.SingleAsync(x => x.Id == id, ct);
        var now = DateTimeOffset.UtcNow;
        db.Attachments.RemoveRange(attachments);
        db.Comments.Remove(comment);
        requirement.Version++; requirement.UpdatedAt = now;
        db.History.Add(new HistoryEntity { Id = Guid.NewGuid(), RequirementId = id, ActorId = context.User.UserId(), Action = "删除评论", Detail = $"删除评论及 {attachments.Count} 个附件", CreatedAt = now });
        var prefix = $"comment-mention:{commentId}:";
        await db.NotificationLogs.Where(x => x.IdempotencyKey != null && x.IdempotencyKey.StartsWith(prefix) && x.State != "sent")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled"), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var attachment in attachments) storage.DeleteFile(attachment.RelativePath);
        return Results.NoContent();
    }

    private static async Task<IResult?> ValidateAsync(RequirementEntity? current, string module, string priority, string statusId, string? assigneeId, string? iterationId, string? parentId, string? reviewerId, Guid? requirementTypeId, HttpContext context, AppDbContext db, CancellationToken ct)
    {
        if (!Priorities.Contains(priority)) return Results.BadRequest(new { message = "优先级无效" });
        if (!await db.Modules.AnyAsync(x => x.Name == module, ct)) return Results.BadRequest(new { message = "归属模块不存在" });
        var status = await db.Statuses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == statusId, ct);
        if (status is null) return Results.BadRequest(new { message = "状态不存在" });
        if (context.User.IsInRole(Roles.Developer) && status.Protected && statusId != current?.StatusId) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (current is not null && statusId != current.StatusId && statusId is "completed" or "closed")
        {
            var incompleteChildren = await db.Requirements.AsNoTracking()
                .CountAsync(x => x.ParentId == current.Id && x.StatusId != "completed" && x.StatusId != "closed", ct);
            if (incompleteChildren > 0)
                return Results.Conflict(new { message = $"父任务仍有 {incompleteChildren} 个未完成的子任务，请先将所有子任务设置为“已完成”或“已关闭”" });
        }
        if (assigneeId is not null && !await db.Users.AnyAsync(x => x.Id == assigneeId && x.IsActive, ct)) return Results.BadRequest(new { message = "处理人不存在或已停用" });
        if (iterationId is not null && !await db.Iterations.AnyAsync(x => x.Id == iterationId, ct)) return Results.BadRequest(new { message = "迭代不存在" });
        if(reviewerId is not null&&!await db.Users.AnyAsync(x=>x.Id==reviewerId,ct))return Results.BadRequest(new{message="验收人不存在或已停用"});
        if(requirementTypeId.HasValue&&!await db.RequirementTypes.AnyAsync(x=>x.Id==requirementTypeId,ct))return Results.BadRequest(new{message="需求单类型不存在或已停用"});
        if (parentId is not null)
        {
            if (parentId == current?.Id) return Results.BadRequest(new { message = "需求不能关联自身" });
            var parent = await db.Requirements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == parentId, ct);
            if (parent is null) return Results.BadRequest(new { message = "父需求不存在" });
            if (parent.ParentId is not null) return Results.BadRequest(new { message = "只能选择顶层需求作为父需求" });
            if (current is not null && current.Children.Count > 0) return Results.BadRequest(new { message = "包含子需求的需求不能再绑定父需求" });
        }
        return null;
    }

    private static bool TryDate(string? value, out DateOnly? date)
    {
        date = null; if (string.IsNullOrWhiteSpace(value)) return true;
        if (!DateOnly.TryParse(value, out var parsed)) return false; date = parsed; return true;
    }
    private static string NormalizeJson(JsonElement? element) => !element.HasValue || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "{}" : element.Value.GetRawText();
}
