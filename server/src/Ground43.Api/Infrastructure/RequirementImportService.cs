using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Ground43.Api.Contracts;
using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Ground43.Api.Infrastructure;

public sealed class RequirementImportException(string message) : Exception(message);

public sealed class RequirementImportService(AppDbContext db, RequirementCodeService codes, ReviewNotificationService notifications, AssignmentNotificationService assignments)
{
    private const int MaxRows = 500;
    private const long MaxBytes = 10L * 1024 * 1024;
    private static readonly Dictionary<string, string> PriorityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["紧急"] = "urgent", ["高"] = "high", ["中"] = "medium", ["低"] = "low",
        ["urgent"] = "urgent", ["high"] = "high", ["medium"] = "medium", ["low"] = "low"
    };

    public async Task<RequirementImportResultDto> RunAsync(IFormFile file, bool commit, string actorId, CancellationToken ct)
    {
        if (file.Length == 0) throw new RequirementImportException("导入文件为空");
        if (file.Length > MaxBytes) throw new RequirementImportException("导入文件不能超过 10MB");
        var table = ReadTable(file);
        if (!table.Headers.Contains("标题")) throw new RequirementImportException("缺少必填列“标题”，请使用需求导出表格作为模板");
        if (table.Rows.Count == 0) throw new RequirementImportException("表格中没有可导入的数据行");
        if (table.Rows.Count > MaxRows) throw new RequirementImportException($"单次最多导入 {MaxRows} 条需求");

        var defaults = await db.RequirementDefaults.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
        var modules = await db.Modules.AsNoTracking().Select(x => x.Name).ToListAsync(ct);
        var moduleMap = modules.ToDictionary(x => x, StringComparer.OrdinalIgnoreCase);
        var statuses = await db.Statuses.AsNoTracking().ToListAsync(ct);
        var statusMap = statuses.SelectMany(x => new[] { (x.Id, x), (x.Name, x) }).GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First().x, StringComparer.OrdinalIgnoreCase);
        var users = await db.Users.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var userMap = users.SelectMany(x => new[] { (x.Id, x), (x.Name, x), (x.Account, x) }).GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First().x, StringComparer.OrdinalIgnoreCase);
        var iterations = await db.Iterations.AsNoTracking().ToListAsync(ct);
        var iterationMap = iterations.SelectMany(x => new[] { (x.Id, x), (x.Name, x) }).GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First().x, StringComparer.OrdinalIgnoreCase);
        var types = await db.RequirementTypes.AsNoTracking().Where(x => x.Enabled).ToListAsync(ct);
        var customFields = await db.CustomFields.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.SortOrder).ToListAsync(ct);
        var typeMap = types.SelectMany(x => new[] { (x.Id.ToString(), x), (x.Name, x) }).GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First().x, StringComparer.OrdinalIgnoreCase);
        var currentIterationId = await db.Iterations.AsNoTracking().Where(x => x.State == "active").OrderByDescending(x => x.StartDate).Select(x => x.Id).FirstOrDefaultAsync(ct);

        var prepared = table.Rows.Select(row => Prepare(row, table.Headers, defaults, moduleMap, statusMap, userMap, iterationMap, typeMap, customFields, currentIterationId)).ToList();
        var invalid = prepared.Count(x => x.Errors.Count > 0);
        if (!commit || invalid > 0) return Result(!commit, prepared, 0);

        var ids = await codes.NextRangeAsync(prepared.Count, ct);
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        for (var index = 0; index < prepared.Count; index++)
        {
            var row = prepared[index]; var id = ids[index];
            var entity = new RequirementEntity
            {
                Id = id, Title = row.Title, Module = row.Module!, Priority = row.Priority!, StatusId = row.StatusId!,
                AssigneeId = row.AssigneeId, CreatorId = actorId, IterationId = row.IterationId, ParentId = null,
                ReviewerId = row.ReviewerId, RequirementTypeId = row.RequirementTypeId, DueDate = row.DueDate,
                Description = row.Description, CustomValuesJson = row.CustomValuesJson, CreatedAt = now, UpdatedAt = now
            };
            entity.SetAssigneeIds(row.AssigneeIds);
            entity.History.Add(new HistoryEntity { Id = Guid.NewGuid(), RequirementId = id, ActorId = actorId, Action = "批量导入需求", Detail = $"从表格第 {row.RowNumber} 行导入", CreatedAt = now });
            db.Requirements.Add(entity);
            await notifications.RecordChangeAsync(entity, null, null, ct);
            await assignments.RecordChangeAsync(entity, null, actorId, true, ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result(false, prepared, prepared.Count);
    }

    private static PreparedRow Prepare(
        ParsedRow row, HashSet<string> headers, RequirementDefaultsEntity defaults,
        Dictionary<string, string> modules, Dictionary<string, StatusEntity> statuses,
        Dictionary<string, UserEntity> users, Dictionary<string, IterationEntity> iterations,
        Dictionary<string, RequirementTypeEntity> types, IReadOnlyList<CustomFieldEntity> customFields, string? currentIterationId)
    {
        var errors = new List<string>();
        var title = row.Get("标题").Trim();
        if (string.IsNullOrWhiteSpace(title)) errors.Add("标题不能为空");
        else if (title.Length > 500) errors.Add("标题不能超过 500 个字符");

        var moduleText = row.Get("模块").Trim();
        if (string.IsNullOrWhiteSpace(moduleText)) moduleText = defaults.Module ?? string.Empty;
        string? module = null;
        if (!modules.TryGetValue(moduleText, out module)) errors.Add($"模块“{moduleText}”不存在");

        var priorityText = row.Get("优先级").Trim();
        if (string.IsNullOrWhiteSpace(priorityText)) priorityText = defaults.Priority;
        string? priority = null;
        if (!PriorityMap.TryGetValue(priorityText, out priority)) errors.Add($"优先级“{priorityText}”无效");

        var statusText = row.Get("状态").Trim();
        if (string.IsNullOrWhiteSpace(statusText)) statusText = defaults.StatusId ?? "todo";
        StatusEntity? status = null;
        if (!statuses.TryGetValue(statusText, out status)) errors.Add($"状态“{statusText}”不存在");

        var assigneeText = row.Get("处理人").Trim();
        string? assigneeId = null;
        var assigneeIds = new List<string>();
        if (!string.IsNullOrWhiteSpace(assigneeText))
        {
            foreach (var account in assigneeText.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var user = users.Values.FirstOrDefault(x => x.Account.Equals(account, StringComparison.OrdinalIgnoreCase));
                // Older exported sheets used a single display name. Accounts always take precedence.
                if (user is null && !assigneeText.Contains(';')) users.TryGetValue(account, out user);
                if (user is not null) assigneeIds.Add(user.Id);
                else errors.Add($"处理人账号“{account}”不存在或已停用");
            }
            assigneeId = assigneeIds.FirstOrDefault();
        }
        else if (!headers.Contains("处理人")) { assigneeId = defaults.AssigneeId; if (assigneeId is not null) assigneeIds.Add(assigneeId); }

        var reviewerText = row.Get("验收人").Trim();
        string? reviewerId = null;
        if (!string.IsNullOrWhiteSpace(reviewerText))
        {
            if (users.TryGetValue(reviewerText, out var reviewer)) reviewerId = reviewer.Id;
            else errors.Add($"验收人“{reviewerText}”不存在");
        }
        else if (!headers.Contains("验收人")) reviewerId = defaults.ReviewerId;

        var iterationText = row.Get("迭代").Trim();
        string? iterationId = null;
        if (!string.IsNullOrWhiteSpace(iterationText) && !iterationText.Equals("需求池", StringComparison.OrdinalIgnoreCase))
        {
            if (iterations.TryGetValue(iterationText, out var iteration)) iterationId = iteration.Id;
            else errors.Add($"迭代“{iterationText}”不存在");
        }
        else if (!headers.Contains("迭代")) iterationId = defaults.IterationMode switch { "current" => currentIterationId, "specific" => defaults.IterationId, _ => null };

        var typeText = row.Get("需求单类型").Trim();
        Guid? typeId = null;
        if (!string.IsNullOrWhiteSpace(typeText))
        {
            if (types.TryGetValue(typeText, out var type)) typeId = type.Id;
            else errors.Add($"需求单类型“{typeText}”不存在或已停用");
        }
        else typeId = defaults.RequirementTypeId;

        var dueText = row.Get("期望完成时间").Trim();
        DateOnly? dueDate = null;
        if (!string.IsNullOrWhiteSpace(dueText) && !TryDate(dueText, out dueDate)) errors.Add($"期望完成时间“{dueText}”格式无效");
        else if (string.IsNullOrWhiteSpace(dueText) && !headers.Contains("期望完成时间") && defaults.DueDateOffsetDays.HasValue) dueDate = DateOnly.FromDateTime(DateTime.Today).AddDays(defaults.DueDateOffsetDays.Value);

        var description = row.Get("需求描述").Trim();
        if (string.IsNullOrWhiteSpace(description)) description = row.Get("描述").Trim();
        if (string.IsNullOrWhiteSpace(description)) description = string.IsNullOrWhiteSpace(defaults.DescriptionTemplate) ? title : defaults.DescriptionTemplate;

        var customValues = new Dictionary<string, object?>();
        foreach (var field in customFields)
        {
            var raw = row.Get(field.Name).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (field.Required) errors.Add($"自定义字段“{field.Name}”不能为空");
                continue;
            }
            var options = JsonSerializer.Deserialize<string[]>(field.OptionsJson) ?? [];
            switch (field.Type)
            {
                case "number":
                    if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.GetCultureInfo("zh-CN"), out var number) || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out number)) customValues[field.Id] = number;
                    else errors.Add($"自定义字段“{field.Name}”必须是数字");
                    break;
                case "date":
                    if (TryDate(raw, out var customDate)) customValues[field.Id] = customDate?.ToString("yyyy-MM-dd");
                    else errors.Add($"自定义字段“{field.Name}”日期格式无效");
                    break;
                case "single":
                    if (options.Length == 0 || options.Contains(raw, StringComparer.OrdinalIgnoreCase)) customValues[field.Id] = raw;
                    else errors.Add($"自定义字段“{field.Name}”选项无效");
                    break;
                case "multi":
                    var selected = raw.Split(['、', ',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (options.Length == 0 || selected.All(value => options.Contains(value, StringComparer.OrdinalIgnoreCase))) customValues[field.Id] = selected;
                    else errors.Add($"自定义字段“{field.Name}”包含无效选项");
                    break;
                case "person":
                    if (users.TryGetValue(raw, out var customUser)) customValues[field.Id] = customUser.Id;
                    else errors.Add($"自定义字段“{field.Name}”成员不存在");
                    break;
                default: customValues[field.Id] = raw; break;
            }
        }

        return new PreparedRow(row.RowNumber, title, module, priority, status?.Id, status?.Name ?? statusText, assigneeId, assigneeText,
            iterationId, iterationText, reviewerId, typeId, typeText, dueDate, dueText, description, JsonSerializer.Serialize(customValues), errors, assigneeIds.Distinct().ToArray());
    }

    private static RequirementImportResultDto Result(bool preview, IReadOnlyList<PreparedRow> rows, int imported) => new(
        preview, rows.Count, rows.Count(x => x.Errors.Count == 0), rows.Count(x => x.Errors.Count > 0), imported,
        rows.Select(x => new RequirementImportRowDto(x.RowNumber, x.Title, x.RequirementTypeText, x.StatusText, x.AssigneeText,
            x.Priority ?? string.Empty, x.Module ?? string.Empty, x.IterationText, x.DueText, x.Errors.Count == 0, x.Errors)).ToArray());

    private static ParsedTable ReadTable(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return extension switch
        {
            ".xlsx" => ReadXlsx(file),
            ".csv" => ReadCsv(file),
            _ => throw new RequirementImportException("仅支持 .xlsx 和 .csv 文件")
        };
    }

    private static ParsedTable ReadXlsx(IFormFile file)
    {
        try
        {
            using var workbook = new XLWorkbook(file.OpenReadStream());
            var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new RequirementImportException("Excel 文件中没有工作表");
            var headerRow = sheet.FirstRowUsed() ?? throw new RequirementImportException("Excel 文件中没有表头");
            var lastColumn = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
            var headers = Enumerable.Range(1, lastColumn).Select(column => headerRow.Cell(column).GetString().Trim()).ToArray();
            ValidateHeaders(headers);
            var rows = new List<ParsedRow>();
            foreach (var source in sheet.RowsUsed().Where(x => x.RowNumber() > headerRow.RowNumber()))
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var column = 1; column <= headers.Length; column++)
                {
                    var cell = source.Cell(column);
                    var value = headers[column - 1] == "期望完成时间" && cell.TryGetValue<DateTime>(out var date)
                        ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : cell.GetFormattedString().Trim();
                    values[headers[column - 1]] = value;
                }
                if (values.Values.All(string.IsNullOrWhiteSpace)) continue;
                rows.Add(new ParsedRow(source.RowNumber(), values));
            }
            return new ParsedTable(headers.ToHashSet(StringComparer.OrdinalIgnoreCase), rows);
        }
        catch (RequirementImportException) { throw; }
        catch (Exception ex) { throw new RequirementImportException($"Excel 文件无法读取：{ex.Message}"); }
    }

    private static ParsedTable ReadCsv(IFormFile file)
    {
        using var parser = new TextFieldParser(file.OpenReadStream(), Encoding.UTF8, true) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields()?.Select(x => x.Trim().TrimStart('\ufeff')).ToArray() ?? throw new RequirementImportException("CSV 文件中没有表头");
        ValidateHeaders(headers);
        var rows = new List<ParsedRow>(); var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++; var fields = parser.ReadFields() ?? [];
            var values = headers.Select((header, index) => (header, value: index < fields.Length ? fields[index].Trim() : string.Empty)).ToDictionary(x => x.header, x => x.value, StringComparer.OrdinalIgnoreCase);
            if (values.Values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(new ParsedRow(rowNumber, values));
        }
        return new ParsedTable(headers.ToHashSet(StringComparer.OrdinalIgnoreCase), rows);
    }

    private static void ValidateHeaders(string[] headers)
    {
        if (headers.Any(string.IsNullOrWhiteSpace)) throw new RequirementImportException("表头中存在空列名");
        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length) throw new RequirementImportException("表头中存在重复列名");
    }

    private static bool TryDate(string value, out DateOnly? date)
    {
        date = null;
        var formats = new[] { "yyyy-MM-dd", "yyyy/M/d", "yyyy.MM.dd", "M/d/yyyy" };
        if (DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)) { date = exact; return true; }
        if (DateOnly.TryParse(value, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.None, out var parsed)) { date = parsed; return true; }
        return false;
    }

    private sealed record ParsedTable(HashSet<string> Headers, List<ParsedRow> Rows);
    private sealed record ParsedRow(int RowNumber, Dictionary<string, string> Values)
    {
        public string Get(string name) => Values.TryGetValue(name, out var value) ? value : string.Empty;
    }
    private sealed record PreparedRow(
        int RowNumber, string Title, string? Module, string? Priority, string? StatusId, string StatusText,
        string? AssigneeId, string AssigneeText, string? IterationId, string IterationText, string? ReviewerId,
        Guid? RequirementTypeId, string RequirementTypeText, DateOnly? DueDate, string DueText, string Description, string CustomValuesJson,
        List<string> Errors, string[] AssigneeIds);
}
