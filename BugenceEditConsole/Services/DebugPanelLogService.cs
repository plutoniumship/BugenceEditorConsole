using System.Data;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public class DebugPanelLogService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<DebugPanelLogService> _logger;

    public DebugPanelLogService(ApplicationDbContext db, ILogger<DebugPanelLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS DebugPanelErrors (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId TEXT NULL,
    Source TEXT NOT NULL,
    ShortDescription TEXT NOT NULL,
    LongDescription TEXT NULL,
    Path TEXT NULL,
    CreatedAtUtc TEXT NOT NULL
);", cancellationToken);
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DebugPanelErrors' AND xtype='U')
CREATE TABLE DebugPanelErrors (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NULL,
    Source NVARCHAR(200) NOT NULL,
    ShortDescription NVARCHAR(500) NOT NULL,
    LongDescription NVARCHAR(MAX) NULL,
    Path NVARCHAR(500) NULL,
    CreatedAtUtc DATETIME2 NOT NULL
);", cancellationToken);
    }

    public async Task LogErrorAsync(
        string source,
        string shortDescription,
        string? longDescription = null,
        string? ownerUserId = null,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureTableAsync(cancellationToken);
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO DebugPanelErrors (OwnerUserId, Source, ShortDescription, LongDescription, Path, CreatedAtUtc)
VALUES (@owner, @source, @short, @long, @path, @created)";
            AddParameter(command, "@owner", ownerUserId);
            AddParameter(command, "@source", Truncate(source, 200));
            AddParameter(command, "@short", Truncate(shortDescription, 500));
            AddParameter(command, "@long", longDescription);
            AddParameter(command, "@path", Truncate(path, 500));
            AddParameter(command, "@created", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist debug log entry for source {Source}.", source);
        }
    }

    private static string Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= length ? value : value[..length];
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
