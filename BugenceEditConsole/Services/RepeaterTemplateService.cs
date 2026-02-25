using System.Data;
using System.Data.Common;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BugenceEditConsole.Services;

public class RepeaterTemplateService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<RepeaterTemplateService> _logger;
    private readonly DebugPanelLogService _debugLogService;
    private const int MaxRepeaterRows = 2000;
    private const int QueryCommandTimeoutSeconds = 120;

    public RepeaterTemplateService(ApplicationDbContext db, ILogger<RepeaterTemplateService> logger, DebugPanelLogService debugLogService)
    {
        _db = db;
        _logger = logger;
        _debugLogService = debugLogService;
    }

    public async Task<string> RenderAsync(string html, string ownerUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        if (!html.Contains("<Repeater-", StringComparison.OrdinalIgnoreCase) &&
            !html.Contains("<SubTemplete-", StringComparison.OrdinalIgnoreCase) &&
            !html.Contains("<Workflow-", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        html = await ReplaceSubTemplatesAsync(html, ownerUserId, cancellationToken);
        html = ReplaceWorkflowTokens(html);

        var tables = await LoadTablesAsync(ownerUserId, cancellationToken);
        foreach (var table in tables)
        {
            var token = table.DguidToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var pattern = new Regex($"<Repeater-{Regex.Escape(token)}>(.*?)<Repeater-{Regex.Escape(token)}\\s*/>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!pattern.IsMatch(html))
            {
                continue;
            }

            var rows = await LoadTableRowsAsync(table, cancellationToken);
            html = pattern.Replace(html, match => BuildRepeatedBlock(match.Groups[1].Value, table, rows));
        }

        var queries = await LoadQuerySelectorsAsync(ownerUserId, cancellationToken);
        foreach (var query in queries)
        {
            if (string.IsNullOrWhiteSpace(query.DguidToken))
            {
                continue;
            }

            var pattern = new Regex($@"<Repeater-{Regex.Escape(query.DguidToken)}>(.*?)<Repeater-{Regex.Escape(query.DguidToken)}\s*/>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!pattern.IsMatch(html))
            {
                continue;
            }

            if (!IsSelectQuery(query.SqlText))
            {
                _logger.LogWarning("Skipping non-select query selector {Id}.", query.Id);
                await _debugLogService.LogErrorAsync(
                    source: "RepeaterTemplateService.QuerySelector",
                    shortDescription: $"Non-select query skipped (QueryId: {query.Id})",
                    longDescription: query.SqlText,
                    ownerUserId: ownerUserId,
                    cancellationToken: cancellationToken);
                continue;
            }

            var rows = await ExecuteQueryAsync(query.SqlText, cancellationToken);
            if (rows.Count == 0)
            {
                html = pattern.Replace(html, match => BuildEmptyRepeatedBlock(match.Groups[1].Value, query.DguidToken, query.Columns));
                continue;
            }

            if (query.Columns.Any(c => string.Equals(c, "*", StringComparison.OrdinalIgnoreCase)))
            {
                var inferredColumns = rows[0]
                    .Keys
                    .Where(key => !string.Equals(key, "DGUID", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (inferredColumns.Count > 0)
                {
                    query.Columns = inferredColumns;
                }
            }

            html = pattern.Replace(html, match => BuildRepeatedBlock(match.Groups[1].Value, query.DguidToken, query.Columns, rows));
        }

        return html;
    }

    private static string ReplaceWorkflowTokens(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || !html.Contains("<Workflow-", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        return Regex.Replace(
            html,
            @"<Workflow-([A-Fa-f0-9-]{32,36})>",
            match =>
            {
                var token = match.Groups[1].Value;
                var normalized = token.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
                if (normalized.Length != 32 || !Regex.IsMatch(normalized, "^[a-f0-9]{32}$"))
                {
                    return string.Empty;
                }

                return $"data-bugence-workflow-dguid=\"{normalized}\" data-bugence-workflow-trigger=\"auto\"";
            },
            RegexOptions.IgnoreCase);
    }

    private async Task<string> ReplaceSubTemplatesAsync(string html, string ownerUserId, CancellationToken cancellationToken)
    {
        var subTemplates = await LoadSubTemplatesAsync(ownerUserId, cancellationToken);
        if (subTemplates.Count == 0)
        {
            return html;
        }

        var updated = html;
        var iterations = 0;
        var changed = true;
        while (changed && iterations < 5)
        {
            changed = false;
            foreach (var sub in subTemplates)
            {
                if (string.IsNullOrWhiteSpace(sub.DguidToken))
                {
                    continue;
                }
                var token = $"<SubTemplete-{sub.DguidToken}>";
                if (!updated.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                updated = Regex.Replace(updated, Regex.Escape(token), sub.TemplateText ?? string.Empty, RegexOptions.IgnoreCase);
                changed = true;
            }
            iterations++;
        }

        return updated;
    }

    private string BuildRepeatedBlock(string template, TableInfo table, List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var buffer = new System.Text.StringBuilder();
        var columnMap = table.Columns.ToDictionary(name => Sanitize(name), name => name, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var rendered = template;
            foreach (var pair in columnMap)
            {
                var token = $"<Repeater-{table.DguidToken}-{pair.Key}>";
                var value = row.TryGetValue(pair.Value, out var raw)
                    ? ConvertValue(raw)
                    : string.Empty;
                rendered = Regex.Replace(rendered, Regex.Escape(token), value, RegexOptions.IgnoreCase);
            }
            buffer.Append(rendered);
        }

        return buffer.ToString();
    }

    private string BuildRepeatedBlock(string template, string dguidToken, List<string> columns, List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var buffer = new System.Text.StringBuilder();
        var containsAll = columns.Any(c => string.Equals(c, "*", StringComparison.OrdinalIgnoreCase));
        var columnMap = columns.Count > 0 && !containsAll
            ? columns.ToDictionary(name => Sanitize(name), name => name, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var rendered = template;
            if (containsAll)
            {
                var allBuilder = new System.Text.StringBuilder();
                foreach (var key in row.Keys)
                {
                    if (string.Equals(key, "DGUID", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    allBuilder.Append(ConvertValue(row[key]));
                }
                rendered = Regex.Replace(rendered, Regex.Escape($"<Repeater-{dguidToken}-AllColumns>"), allBuilder.ToString(), RegexOptions.IgnoreCase);
            }
            else if (columnMap.Count == 0)
            {
                foreach (var key in row.Keys)
                {
                    var token = $"<Repeater-{dguidToken}-{Sanitize(key)}>";
                    var value = ConvertValue(row[key]);
                    rendered = Regex.Replace(rendered, Regex.Escape(token), value, RegexOptions.IgnoreCase);
                }
            }
            else
            {
                foreach (var pair in columnMap)
                {
                    var token = $"<Repeater-{dguidToken}-{pair.Key}>";
                    var value = row.TryGetValue(pair.Value, out var raw)
                        ? ConvertValue(raw)
                        : string.Empty;
                    rendered = Regex.Replace(rendered, Regex.Escape(token), value, RegexOptions.IgnoreCase);
                }
            }
            buffer.Append(rendered);
        }

        return buffer.ToString();
    }

    private string BuildEmptyRepeatedBlock(string template, string dguidToken, List<string> columns)
    {
        var rendered = template;
        rendered = Regex.Replace(rendered, Regex.Escape($"<Repeater-{dguidToken}-AllColumns>"), string.Empty, RegexOptions.IgnoreCase);

        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column) || column == "*")
            {
                continue;
            }

            var token = $"<Repeater-{dguidToken}-{Sanitize(column)}>";
            rendered = Regex.Replace(rendered, Regex.Escape(token), string.Empty, RegexOptions.IgnoreCase);
        }

        // Remove any unresolved field token for the same repeater so static markup survives.
        rendered = Regex.Replace(rendered, $@"<Repeater-{Regex.Escape(dguidToken)}-[^>]+>", string.Empty, RegexOptions.IgnoreCase);
        return rendered;
    }

    private async Task<List<TableInfo>> LoadTablesAsync(string ownerUserId, CancellationToken cancellationToken)
    {
        var tables = new Dictionary<Guid, TableInfo>();
        await EnsureMetadataTablesAsync(cancellationToken);
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, TableName FROM ApplicationTables WHERE OwnerUserId = @owner";
            AddParameter(command, "@owner", ownerUserId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
                var token = id.ToString("N");
                tables[id] = new TableInfo
                {
                    Id = id,
                    TableName = reader.GetString(1),
                    DguidToken = token.Length >= 16 ? token[..16] : token
                };
            }
        }

        if (tables.Count == 0)
        {
            return new List<TableInfo>();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ApplicationTableId, ColumnName FROM ApplicationTableColumns WHERE ApplicationTableId IN (" +
                                  string.Join(", ", tables.Keys.Select(k => $"'{k}'")) +
                                  ")";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
                if (!tables.TryGetValue(id, out var table))
                {
                    continue;
                }
                var name = reader.GetString(1);
                if (string.Equals(name, "DGUID", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                table.Columns.Add(name);
            }
        }

        return tables.Values.ToList();
    }

    private async Task<List<QueryInfo>> LoadQuerySelectorsAsync(string ownerUserId, CancellationToken cancellationToken)
    {
        var list = new List<QueryInfo>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using (var fill = connection.CreateCommand())
        {
            if (isSqlite)
            {
                fill.CommandText = "UPDATE DatabaseQuerySelectors SET DGUID = lower(hex(randomblob(16))) WHERE OwnerUserId = @owner AND (DGUID IS NULL OR DGUID = '')";
            }
            else
            {
                fill.CommandText = "UPDATE DatabaseQuerySelectors SET DGUID = NEWID() WHERE OwnerUserId = @owner AND (DGUID IS NULL OR DGUID = '00000000-0000-0000-0000-000000000000')";
            }
            AddParameter(fill, "@owner", ownerUserId);
            await fill.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, DGUID, SqlText FROM DatabaseQuerySelectors WHERE OwnerUserId = @owner ORDER BY CreatedAtUtc DESC";
        AddParameter(command, "@owner", ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dguid = isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString();
            var token = dguid.Replace("-", string.Empty);
            var sql = reader.GetString(2);
            list.Add(new QueryInfo
            {
                Id = reader.GetInt32(0),
                SqlText = sql,
                DguidToken = token.Length >= 16 ? token[..16] : token,
                Columns = ParseSelectedColumns(sql)
            });
        }

        return list;
    }

    private async Task<List<SubTemplateInfo>> LoadSubTemplatesAsync(string ownerUserId, CancellationToken cancellationToken)
    {
        var list = new List<SubTemplateInfo>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using (var fill = connection.CreateCommand())
        {
            if (isSqlite)
            {
                fill.CommandText = "UPDATE TempleteViewers SET DGUID = lower(hex(randomblob(16))) WHERE OwnerUserId = @owner AND (DGUID IS NULL OR DGUID = '')";
            }
            else
            {
                fill.CommandText = "UPDATE TempleteViewers SET DGUID = NEWID() WHERE OwnerUserId = @owner AND (DGUID IS NULL OR DGUID = '00000000-0000-0000-0000-000000000000')";
            }
            AddParameter(fill, "@owner", ownerUserId);
            await fill.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, DGUID, TemplateText FROM TempleteViewers WHERE OwnerUserId = @owner AND ViewerType = @type";
        AddParameter(command, "@owner", ownerUserId);
        AddParameter(command, "@type", "Sub Templete");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dguid = isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString();
            var token = dguid.Replace("-", string.Empty);
            list.Add(new SubTemplateInfo
            {
                Id = reader.GetInt32(0),
                TemplateText = reader.GetString(2),
                DguidToken = token.Length >= 16 ? token[..16] : token
            });
        }

        return list;
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(string sql, CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = QueryCommandTimeoutSeconds;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
                if (rows.Count >= MaxRepeaterRows)
                {
                    _logger.LogWarning("Repeater query row limit reached ({Limit}). Remaining rows were skipped.", MaxRepeaterRows);
                    break;
                }
            }

            return rows;
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex, "Repeater query failed. SQL: {Sql}", sql);
            await _debugLogService.LogErrorAsync(
                source: "RepeaterTemplateService.ExecuteQuery",
                shortDescription: ex.Message,
                longDescription: ex.ToString(),
                path: "QuerySelector SQL",
                cancellationToken: cancellationToken);
            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Repeater query failed unexpectedly. SQL: {Sql}", sql);
            await _debugLogService.LogErrorAsync(
                source: "RepeaterTemplateService.ExecuteQuery",
                shortDescription: ex.Message,
                longDescription: ex.ToString(),
                path: "QuerySelector SQL",
                cancellationToken: cancellationToken);
            return rows;
        }
    }

    private static bool IsSelectQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

        var start = SkipLeadingTrivia(sql, 0);
        if (start >= sql.Length)
        {
            return false;
        }

        return StartsWithKeyword(sql, start, "select") || StartsWithKeyword(sql, start, "with");
    }

    private static List<string> ParseSelectedColumns(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new List<string>();
        }

        if (!TryExtractTopLevelSelectProjection(sql, out var projection))
        {
            return new List<string>();
        }

        var parts = SplitTopLevelComma(projection);
        if (parts.Count == 0)
        {
            return new List<string>();
        }

        var columns = new List<string>();
        foreach (var part in parts)
        {
            var cleaned = part.Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            if (cleaned == "*" || cleaned.EndsWith(".*", StringComparison.Ordinal))
            {
                return new List<string> { "*" };
            }

            var aliasMatch = Regex.Match(cleaned, "(?is)\\s+as\\s+(?<alias>(\\[[^\\]]+\\]|`[^`]+`|\"[^\"]+\"|[A-Za-z_][A-Za-z0-9_]*))\\s*$");
            var name = aliasMatch.Success ? aliasMatch.Groups["alias"].Value : cleaned;

            if (!aliasMatch.Success)
            {
                var trailingAlias = Regex.Match(cleaned, "(?is)\\s+(?<alias>(\\[[^\\]]+\\]|`[^`]+`|\"[^\"]+\"|[A-Za-z_][A-Za-z0-9_]*))\\s*$");
                if (trailingAlias.Success && !cleaned.EndsWith(")", StringComparison.Ordinal))
                {
                    name = trailingAlias.Groups["alias"].Value;
                }
                else if (cleaned.Contains('.'))
                {
                    var segments = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    name = segments[^1];
                }
            }

            name = name.Trim().Trim('[', ']', '`', '"');
            if (!string.IsNullOrWhiteSpace(name) && name != "*")
            {
                columns.Add(name);
            }
        }

        return columns;
    }

    private static bool TryExtractTopLevelSelectProjection(string sql, out string projection)
    {
        projection = string.Empty;
        var selectIndex = IndexOfTopLevelKeyword(sql, "select", 0);
        if (selectIndex < 0)
        {
            return false;
        }

        var fromIndex = IndexOfTopLevelKeyword(sql, "from", selectIndex + 6);
        if (fromIndex <= selectIndex)
        {
            return false;
        }

        projection = sql.Substring(selectIndex + 6, fromIndex - (selectIndex + 6)).Trim();
        return projection.Length > 0;
    }

    private static List<string> SplitTopLevelComma(string text)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return list;
        }

        var start = 0;
        var depth = 0;
        var inSingle = false;
        var inDouble = false;
        var inBracket = false;
        var inBacktick = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (!inSingle && !inDouble && !inBracket && !inBacktick)
            {
                if (ch == '-' && next == '-')
                {
                    i += 2;
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }

                if (ch == '/' && next == '*')
                {
                    i += 2;
                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                    {
                        i++;
                    }
                    i++;
                    continue;
                }
            }

            if (!inDouble && !inBracket && !inBacktick && ch == '\'')
            {
                inSingle = !inSingle;
                continue;
            }
            if (!inSingle && !inBracket && !inBacktick && ch == '"')
            {
                inDouble = !inDouble;
                continue;
            }
            if (!inSingle && !inDouble && !inBacktick && ch == '[')
            {
                inBracket = true;
                continue;
            }
            if (inBracket && ch == ']')
            {
                inBracket = false;
                continue;
            }
            if (!inSingle && !inDouble && !inBracket && ch == '`')
            {
                inBacktick = !inBacktick;
                continue;
            }

            if (inSingle || inDouble || inBracket || inBacktick)
            {
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }
            if (ch == ')' && depth > 0)
            {
                depth--;
                continue;
            }
            if (ch == ',' && depth == 0)
            {
                list.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (start <= text.Length)
        {
            list.Add(text[start..]);
        }

        return list;
    }

    private static int IndexOfTopLevelKeyword(string sql, string keyword, int startIndex)
    {
        var depth = 0;
        var inSingle = false;
        var inDouble = false;
        var inBracket = false;
        var inBacktick = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = Math.Max(0, startIndex); i < sql.Length; i++)
        {
            var ch = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                {
                    inLineComment = false;
                }
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (!inSingle && !inDouble && !inBracket && !inBacktick)
            {
                if (ch == '-' && next == '-')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }
                if (ch == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (!inDouble && !inBracket && !inBacktick && ch == '\'')
            {
                inSingle = !inSingle;
                continue;
            }
            if (!inSingle && !inBracket && !inBacktick && ch == '"')
            {
                inDouble = !inDouble;
                continue;
            }
            if (!inSingle && !inDouble && !inBacktick && ch == '[')
            {
                inBracket = true;
                continue;
            }
            if (inBracket && ch == ']')
            {
                inBracket = false;
                continue;
            }
            if (!inSingle && !inDouble && !inBracket && ch == '`')
            {
                inBacktick = !inBacktick;
                continue;
            }

            if (inSingle || inDouble || inBracket || inBacktick)
            {
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }
            if (ch == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth == 0 && StartsWithKeyword(sql, i, keyword))
            {
                return i;
            }
        }

        return -1;
    }

    private static int SkipLeadingTrivia(string sql, int startIndex)
    {
        var i = Math.Max(0, startIndex);
        while (i < sql.Length)
        {
            while (i < sql.Length && (char.IsWhiteSpace(sql[i]) || sql[i] == ';'))
            {
                i++;
            }

            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                i += 2;
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }
                continue;
            }

            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    i++;
                }
                i = Math.Min(i + 2, sql.Length);
                continue;
            }

            break;
        }

        return i;
    }

    private static bool StartsWithKeyword(string value, int startIndex, string keyword)
    {
        if (startIndex < 0 || startIndex + keyword.Length > value.Length)
        {
            return false;
        }

        if (!value.AsSpan(startIndex, keyword.Length).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var beforeOk = startIndex == 0 || !IsSqlIdentifierChar(value[startIndex - 1]);
        var afterIndex = startIndex + keyword.Length;
        var afterOk = afterIndex >= value.Length || !IsSqlIdentifierChar(value[afterIndex]);
        return beforeOk && afterOk;
    }

    private static bool IsSqlIdentifierChar(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_' || ch == '$';
    }

    private async Task<List<Dictionary<string, object?>>> LoadTableRowsAsync(TableInfo table, CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object?>>();
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var quote = isSqlite ? "\"" : "[";
        var quoteEnd = isSqlite ? "\"" : "]";
        var tableIdentifier = isSqlite ? $"\"{table.TableName}\"" : $"[{table.TableName}]";
        var columns = table.Columns.ToList();
        var selectCols = new List<string> { $"{quote}DGUID{quoteEnd}" };
        selectCols.AddRange(columns.Select(c => $"{quote}{c}{quoteEnd}"));

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", selectCols)} FROM {tableIdentifier}";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }

        return rows;
    }

    private async Task EnsureMetadataTablesAsync(CancellationToken cancellationToken)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ApplicationTables (
    Id TEXT PRIMARY KEY,
    OwnerUserId TEXT NOT NULL,
    TableName TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ApplicationTableColumns (
    Id TEXT PRIMARY KEY,
    ApplicationTableId TEXT NOT NULL,
    ColumnName TEXT NOT NULL,
    DataType TEXT NOT NULL,
    Length INTEGER NULL,
    Precision INTEGER NULL,
    Scale INTEGER NULL,
    IsNullable INTEGER NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ApplicationRecords (
    RecordId INTEGER PRIMARY KEY AUTOINCREMENT,
    ApplicationTableId TEXT NOT NULL,
    DGUID TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);", cancellationToken);
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApplicationTables' AND xtype='U')
CREATE TABLE ApplicationTables (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    TableName NVARCHAR(128) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL
);
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApplicationTableColumns' AND xtype='U')
CREATE TABLE ApplicationTableColumns (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    ApplicationTableId UNIQUEIDENTIFIER NOT NULL,
    ColumnName NVARCHAR(128) NOT NULL,
    DataType NVARCHAR(64) NOT NULL,
    Length INT NULL,
    Precision INT NULL,
    Scale INT NULL,
    IsNullable BIT NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL
);
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApplicationRecords' AND xtype='U')
CREATE TABLE ApplicationRecords (
    RecordId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ApplicationTableId UNIQUEIDENTIFIER NOT NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL
);", cancellationToken);
    }

    private static string Sanitize(string value)
    {
        var buffer = new System.Text.StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer.Append(ch);
            }
        }
        return buffer.ToString();
    }

    private static string ConvertValue(object? value)
    {
        if (value == null || value is DBNull)
        {
            return string.Empty;
        }

        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed class TableInfo
    {
        public Guid Id { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string DguidToken { get; set; } = string.Empty;
        public List<string> Columns { get; } = new();
    }

    private sealed class QueryInfo
    {
        public int Id { get; set; }
        public string SqlText { get; set; } = string.Empty;
        public string DguidToken { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new();
    }

    private sealed class SubTemplateInfo
    {
        public int Id { get; set; }
        public string TemplateText { get; set; } = string.Empty;
        public string DguidToken { get; set; } = string.Empty;
    }
}
