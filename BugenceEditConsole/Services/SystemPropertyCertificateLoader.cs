using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace BugenceEditConsole.Services;

public static class SystemPropertyCertificateLoader
{
    public sealed class WebhookCertificateSettings
    {
        public string Endpoint { get; init; } = string.Empty;
        public string ApiKey { get; init; } = string.Empty;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Endpoint) &&
            !string.IsNullOrWhiteSpace(ApiKey);
    }

    public static async Task<WebhookCertificateSettings?> TryLoadWebhookSettingsAsync(
        ApplicationDbContext db,
        ISensitiveDataProtector protector,
        string? ownerUserId = null,
        Guid? companyId = null,
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

        // 1) Tenant scoped (owner + company), 2) owner scoped, 3) latest global.
        command.CommandText = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? @"SELECT Host, Port, RouteUrl, Username, PasswordEncrypted
FROM SystemProperties
WHERE lower(Category) = @category
  AND (
        (@owner IS NOT NULL AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company))
        OR (@owner IS NOT NULL AND OwnerUserId = @owner)
        OR (@owner IS NULL)
      )
ORDER BY
  CASE
    WHEN @owner IS NOT NULL AND OwnerUserId = @owner AND CompanyId = @company THEN 0
    WHEN @owner IS NOT NULL AND OwnerUserId = @owner AND CompanyId IS NULL THEN 1
    WHEN @owner IS NOT NULL AND OwnerUserId = @owner THEN 2
    ELSE 3
  END,
  UpdatedAtUtc DESC, Id DESC
LIMIT 1"
            : @"SELECT TOP 1 Host, Port, RouteUrl, Username, PasswordEncrypted
FROM SystemProperties
WHERE lower(Category) = @category
  AND (
        (@owner IS NOT NULL AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company))
        OR (@owner IS NOT NULL AND OwnerUserId = @owner)
        OR (@owner IS NULL)
      )
ORDER BY
  CASE
    WHEN @owner IS NOT NULL AND OwnerUserId = @owner AND CompanyId = @company THEN 0
    WHEN @owner IS NOT NULL AND OwnerUserId = @owner AND CompanyId IS NULL THEN 1
    WHEN @owner IS NOT NULL AND OwnerUserId = @owner THEN 2
    ELSE 3
  END,
  UpdatedAtUtc DESC, Id DESC";
        AddParameter(command, "@category", "certificatewebhook");
        AddParameter(command, "@owner", ownerUserId);
        AddParameter(command, "@company", companyId?.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var host = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        var port = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var route = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        var username = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
        var encrypted = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

        var apiKey = SafeUnprotect(protector, encrypted);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = username;
        }

        return new WebhookCertificateSettings
        {
            Endpoint = BuildEndpoint(host, port, route),
            ApiKey = apiKey
        };
    }

    private static string BuildEndpoint(string host, string port, string routeUrl)
    {
        if (Uri.TryCreate(routeUrl, UriKind.Absolute, out var absoluteRoute))
        {
            return absoluteRoute.ToString();
        }

        if (Uri.TryCreate(host, UriKind.Absolute, out var absoluteHost))
        {
            var baseUri = absoluteHost;
            if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var parsedPort) && parsedPort > 0)
            {
                var builder = new UriBuilder(baseUri) { Port = parsedPort };
                baseUri = builder.Uri;
            }

            var path = string.IsNullOrWhiteSpace(routeUrl) ? string.Empty : routeUrl.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                return baseUri.ToString();
            }

            return new Uri(baseUri, path.StartsWith('/') ? path : "/" + path).ToString();
        }

        if (!string.IsNullOrWhiteSpace(host))
        {
            var normalizedHost = host.Trim();
            var schemeHost = normalizedHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             normalizedHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? normalizedHost
                : $"https://{normalizedHost}";

            var builder = new UriBuilder(schemeHost);
            if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var parsedPort) && parsedPort > 0)
            {
                builder.Port = parsedPort;
            }

            if (!string.IsNullOrWhiteSpace(routeUrl))
            {
                builder.Path = routeUrl.StartsWith('/') ? routeUrl : "/" + routeUrl;
            }

            return builder.Uri.ToString();
        }

        return string.Empty;
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
}

