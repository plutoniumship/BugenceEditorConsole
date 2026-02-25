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
        command.CommandText = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? @"SELECT Username, PasswordEncrypted, RouteUrl
FROM SystemProperties
WHERE Category = @category
ORDER BY UpdatedAtUtc DESC, Id DESC
LIMIT 1"
            : @"SELECT TOP 1 Username, PasswordEncrypted, RouteUrl
FROM SystemProperties
WHERE Category = @category
ORDER BY UpdatedAtUtc DESC, Id DESC";

        AddParameter(command, "@category", "OAuthGoogle");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var encryptedSecret = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var secret = SafeUnprotect(protector, encryptedSecret);

        return new GoogleOAuthSettings
        {
            ClientId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            ClientSecret = secret,
            RedirectUri = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
        };
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
}
