using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Text.Json;

namespace BugenceEditConsole.Services;

public static class SystemPropertySmtpLoader
{
    public sealed class SmtpProfile
    {
        public int Id { get; init; }
        public string Dguid { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; } = 587;
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string FromAddress { get; init; } = string.Empty;
        public string FromName { get; init; } = "Bugence";
        public bool EnableSsl { get; init; } = true;
    }

    public static async Task<List<SmtpProfile>> LoadProfilesAsync(
        ApplicationDbContext db,
        ISensitiveDataProtector protector,
        string ownerUserId,
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(db, ownerUserId, companyId, cancellationToken);
        return rows.Select(row => MapRow(row, protector)).ToList();
    }

    public static async Task<SmtpProfile?> FindProfileAsync(
        ApplicationDbContext db,
        ISensitiveDataProtector protector,
        string ownerUserId,
        Guid? companyId,
        string? dguid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dguid))
        {
            return null;
        }

        var target = NormalizeDguid(dguid);
        var rows = await LoadRowsAsync(db, ownerUserId, companyId, cancellationToken);
        var match = rows.FirstOrDefault(row => NormalizeDguid(row.Dguid) == target);
        return match == null ? null : MapRow(match, protector);
    }

    public static async Task<SmtpProfile?> FindDefaultProfileAsync(
        ApplicationDbContext db,
        ISensitiveDataProtector protector,
        string ownerUserId,
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(db, ownerUserId, companyId, cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var preferred = rows.FirstOrDefault(row =>
            string.Equals(row.Name?.Trim(), "Default SMTP", StringComparison.OrdinalIgnoreCase));
        var selected = preferred ?? rows[0];
        return MapRow(selected, protector);
    }

    private static async Task<List<SmtpRow>> LoadRowsAsync(
        ApplicationDbContext db,
        string ownerUserId,
        Guid? companyId,
        CancellationToken cancellationToken)
    {
        var list = new List<SmtpRow>();
        var provider = db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (!await SystemPropertiesTableExistsAsync(connection, provider, cancellationToken))
        {
            return list;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = isSqlite
            ? @"SELECT Id, DGUID, Name, Username, PasswordEncrypted, Host, Port, RouteUrl, Notes
FROM SystemProperties
WHERE OwnerUserId = @owner
  AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)
  AND lower(Category) = @category
ORDER BY UpdatedAtUtc DESC, Id DESC"
            : @"SELECT Id, DGUID, Name, Username, PasswordEncrypted, Host, Port, RouteUrl, Notes
FROM SystemProperties
WHERE OwnerUserId = @owner
  AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)
  AND lower(Category) = @category
ORDER BY UpdatedAtUtc DESC, Id DESC";

        AddParameter(command, "@owner", ownerUserId);
        AddParameter(command, "@company", companyId.HasValue ? (isSqlite ? companyId.Value.ToString() : companyId.Value) : null);
        AddParameter(command, "@category", "smtp");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dguid = reader.IsDBNull(1)
                ? string.Empty
                : (isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString("N"));

            list.Add(new SmtpRow
            {
                Id = reader.GetInt32(0),
                Dguid = dguid,
                Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Username = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                PasswordEncrypted = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Host = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Port = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                RouteUrl = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                Notes = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
            });
        }

        return list;
    }

    private static SmtpProfile MapRow(SmtpRow row, ISensitiveDataProtector protector)
    {
        var defaults = ParseSmtpNotes(row.Notes);
        var decryptedPassword = SafeUnprotect(protector, row.PasswordEncrypted);
        var port = int.TryParse(row.Port, out var parsedPort) && parsedPort > 0 ? parsedPort : 587;
        var fromAddress = !string.IsNullOrWhiteSpace(defaults.FromAddress)
            ? defaults.FromAddress
            : (IsEmail(row.RouteUrl) ? row.RouteUrl : row.Username);
        var fromName = !string.IsNullOrWhiteSpace(defaults.FromName) ? defaults.FromName : (string.IsNullOrWhiteSpace(row.Name) ? "Bugence" : row.Name);

        return new SmtpProfile
        {
            Id = row.Id,
            Dguid = NormalizeDguid(row.Dguid),
            Name = row.Name,
            Host = row.Host,
            Port = port,
            Username = row.Username,
            Password = decryptedPassword,
            FromAddress = fromAddress,
            FromName = fromName,
            EnableSsl = defaults.EnableSsl ?? true
        };
    }

    private static (string? FromAddress, string? FromName, bool? EnableSsl) ParseSmtpNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return (null, null, null);
        }

        var trimmed = notes.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return (null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            string? fromAddress = root.TryGetProperty("fromAddress", out var fromAddressElement) ? fromAddressElement.GetString() : null;
            string? fromName = root.TryGetProperty("fromName", out var fromNameElement) ? fromNameElement.GetString() : null;
            bool? enableSsl = root.TryGetProperty("enableSsl", out var sslElement) && sslElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? sslElement.GetBoolean()
                : null;
            return (fromAddress, fromName, enableSsl);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static bool IsEmail(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Contains('@', StringComparison.Ordinal);

    private static string NormalizeDguid(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

    private static string SafeUnprotect(ISensitiveDataProtector protector, string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            return protector.Unprotect(protectedValue);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<bool> SystemPropertiesTableExistsAsync(DbConnection connection, string providerName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SystemProperties'"
            : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'SystemProperties'";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) > 0;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed class SmtpRow
    {
        public int Id { get; init; }
        public string Dguid { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string PasswordEncrypted { get; init; } = string.Empty;
        public string Host { get; init; } = string.Empty;
        public string Port { get; init; } = string.Empty;
        public string RouteUrl { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;
    }
}

