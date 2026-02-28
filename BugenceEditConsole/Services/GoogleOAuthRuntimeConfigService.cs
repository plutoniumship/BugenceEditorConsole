using BugenceEditConsole.Data;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public interface IGoogleOAuthRuntimeConfigService
{
    Task<GoogleOAuthRuntimeConfig> ResolveAsync(CancellationToken cancellationToken = default);
}

public sealed record GoogleOAuthRuntimeConfig(
    bool IsConfigured,
    string ClientId,
    string ClientSecret,
    string CallbackPath,
    string Source,
    string? Reason);

public sealed class GoogleOAuthRuntimeConfigService : IGoogleOAuthRuntimeConfigService
{
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _db;
    private readonly ISensitiveDataProtector _protector;

    public GoogleOAuthRuntimeConfigService(
        IConfiguration configuration,
        ApplicationDbContext db,
        ISensitiveDataProtector protector)
    {
        _configuration = configuration;
        _db = db;
        _protector = protector;
    }

    public async Task<GoogleOAuthRuntimeConfig> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var configClientId = Normalize(_configuration["Authentication:Google:ClientId"]);
        var configClientSecret = Normalize(_configuration["Authentication:Google:ClientSecret"]);
        var callbackPath = "/signin-google";

        try
        {
            var dbGoogle = await SystemPropertyOAuthLoader.TryLoadGoogleOAuthSettingsAsync(_db, _protector, cancellationToken);
            if (dbGoogle?.IsConfigured == true)
            {
                if (TryNormalizeCallbackPath(dbGoogle.RedirectUri, out var dbCallbackPath))
                {
                    return new GoogleOAuthRuntimeConfig(
                        IsConfigured: true,
                        ClientId: Normalize(dbGoogle.ClientId),
                        ClientSecret: Normalize(dbGoogle.ClientSecret),
                        CallbackPath: dbCallbackPath,
                        Source: "SystemProperties",
                        Reason: null);
                }

                return new GoogleOAuthRuntimeConfig(
                    IsConfigured: false,
                    ClientId: string.Empty,
                    ClientSecret: string.Empty,
                    CallbackPath: callbackPath,
                    Source: "SystemProperties",
                    Reason: "OAuthGoogle Redirect URI must end with /signin-google.");
            }
        }
        catch (Exception ex)
        {
            return new GoogleOAuthRuntimeConfig(
                IsConfigured: false,
                ClientId: string.Empty,
                ClientSecret: string.Empty,
                CallbackPath: callbackPath,
                Source: "SystemProperties",
                Reason: $"Failed to load OAuthGoogle record: {ex.Message}");
        }

        if (IsPlaceholder(configClientId) || IsPlaceholder(configClientSecret))
        {
            return new GoogleOAuthRuntimeConfig(
                IsConfigured: false,
                ClientId: string.Empty,
                ClientSecret: string.Empty,
                CallbackPath: callbackPath,
                Source: "Configuration",
                Reason: "Google OAuth credentials are missing or placeholder values.");
        }

        return new GoogleOAuthRuntimeConfig(
            IsConfigured: !string.IsNullOrWhiteSpace(configClientId) && !string.IsNullOrWhiteSpace(configClientSecret),
            ClientId: configClientId,
            ClientSecret: configClientSecret,
            CallbackPath: callbackPath,
            Source: "Configuration",
            Reason: string.IsNullOrWhiteSpace(configClientId) || string.IsNullOrWhiteSpace(configClientSecret)
                ? "Google OAuth credentials are empty in configuration."
                : null);
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static bool IsPlaceholder(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return normalized.StartsWith("YOUR_GOOGLE_", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("YOUR_CLIENT_ID", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("YOUR_CLIENT_SECRET", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("changeme", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeCallbackPath(string? routeUrl, out string callbackPath)
    {
        callbackPath = "/signin-google";
        if (string.IsNullOrWhiteSpace(routeUrl))
        {
            return false;
        }

        if (Uri.TryCreate(routeUrl, UriKind.Absolute, out var redirectUri) &&
            string.Equals(redirectUri.AbsolutePath, "/signin-google", StringComparison.OrdinalIgnoreCase))
        {
            callbackPath = redirectUri.AbsolutePath;
            return true;
        }

        if (string.Equals(routeUrl.Trim(), "/signin-google", StringComparison.OrdinalIgnoreCase))
        {
            callbackPath = "/signin-google";
            return true;
        }

        return false;
    }
}

public sealed class GoogleOAuthNamedOptionsSetup : IPostConfigureOptions<OAuthOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GoogleOAuthNamedOptionsSetup> _logger;

    public GoogleOAuthNamedOptionsSetup(
        IServiceScopeFactory scopeFactory,
        ILogger<GoogleOAuthNamedOptionsSetup> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void PostConfigure(string? name, OAuthOptions options)
    {
        if (!string.Equals(name, "Google", StringComparison.Ordinal))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IGoogleOAuthRuntimeConfigService>();
        var resolved = resolver.ResolveAsync().GetAwaiter().GetResult();

        // Keep the Google scheme operational even when credentials are missing,
        // so /Auth/Login never 500s due to options validation.
        var safeClientId = string.IsNullOrWhiteSpace(resolved.ClientId) ? "__google_not_configured__" : resolved.ClientId;
        var safeClientSecret = string.IsNullOrWhiteSpace(resolved.ClientSecret) ? "__google_not_configured__" : resolved.ClientSecret;

        options.ClientId = safeClientId;
        options.ClientSecret = safeClientSecret;
        options.CallbackPath = new PathString(string.IsNullOrWhiteSpace(resolved.CallbackPath) ? "/signin-google" : resolved.CallbackPath);

        if (resolved.IsConfigured)
        {
            _logger.LogInformation("Google OAuth options applied from {Source}. CallbackPath={CallbackPath}", resolved.Source, options.CallbackPath);
        }
        else
        {
            _logger.LogWarning("Google OAuth options not configured. Source={Source}. Reason={Reason}", resolved.Source, resolved.Reason ?? "Unknown");
        }
    }
}
