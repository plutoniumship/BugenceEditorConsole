using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace BugenceEditConsole.Services;

public static class SystemPropertyOAuthLoader
{
    public sealed class GoogleOAuthSettings
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ClientId) &&
            !string.IsNullOrWhiteSpace(ClientSecret) &&
            !string.IsNullOrWhiteSpace(RedirectUri);
    }

    public static async Task<GoogleOAuthSettings?> TryLoadGoogleOAuthSettingsAsync(
        ApplicationDbContext db,
        ISensitiveDataProtector protector,
        CancellationToken cancellationToken = default)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (!await SystemPropertiesTableExistsAsync(connection, provider, cancellationToken))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = BuildOAuthQuery(provider, includeInactiveFilter: true);

        AddParameter(command, "@category", "OAuthGoogle");

        DbDataReader reader;
        try
        {
            reader = await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (DbException ex) when (IsMissingInActiveColumn(ex))
        {
            // Backward compatibility for older deployments where SystemProperties
            // does not yet have InActive. Retry without that predicate.
            command.CommandText = BuildOAuthQuery(provider, includeInactiveFilter: false);
            reader = await command.ExecuteReaderAsync(cancellationToken);
        }

        await using (reader)
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var clientId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var encryptedSecret = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var redirectUri = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var secret = SafeUnprotect(protector, encryptedSecret);

                var candidate = new GoogleOAuthSettings
                {
                    ClientId = clientId,
                    ClientSecret = secret,
                    RedirectUri = redirectUri
                };
                if (candidate.IsConfigured)
                {
                    return candidate;
                }
            }
        }

        return null;
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

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string BuildOAuthQuery(string provider, bool includeInactiveFilter)
    {
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var inactiveFilter = includeInactiveFilter ? "  AND ifnull(InActive, 0) = 0\n" : string.Empty;
            return $@"SELECT Username, PasswordEncrypted, RouteUrl
FROM SystemProperties
WHERE Category = @category
{inactiveFilter}  AND ifnull(trim(Username), '') <> ''
  AND ifnull(trim(PasswordEncrypted), '') <> ''
  AND ifnull(trim(RouteUrl), '') <> ''
ORDER BY UpdatedAtUtc DESC, Id DESC
LIMIT 20";
        }

        var sqlInactiveFilter = includeInactiveFilter ? "  AND ISNULL(InActive, 0) = 0\n" : string.Empty;
        return $@"SELECT TOP 20 Username, PasswordEncrypted, RouteUrl
FROM SystemProperties
WHERE Category = @category
{sqlInactiveFilter}  AND LTRIM(RTRIM(ISNULL(Username, ''))) <> ''
  AND LTRIM(RTRIM(ISNULL(PasswordEncrypted, ''))) <> ''
  AND LTRIM(RTRIM(ISNULL(RouteUrl, ''))) <> ''
ORDER BY UpdatedAtUtc DESC, Id DESC";
    }

    private static bool IsMissingInActiveColumn(DbException exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.Contains("no such column", StringComparison.OrdinalIgnoreCase)
            && message.Contains("InActive", StringComparison.OrdinalIgnoreCase);
    }
}
