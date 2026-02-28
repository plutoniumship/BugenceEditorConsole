using BugenceEditConsole.Data;
using BugenceEditConsole.Data.Seed;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

var builder = WebApplication.CreateBuilder(args);

// Allow large uploads (many files/folders)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = long.MaxValue;
});
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = long.MaxValue;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
    options.ValueCountLimit = int.MaxValue;
    options.KeyLengthLimit = int.MaxValue;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBoundaryLengthLimit = int.MaxValue;
    options.MultipartHeadersCountLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
    options.MemoryBufferThreshold = 1024 * 1024 * 128; // 128MB buffer before disk
});

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString)
        .ConfigureWarnings(warnings =>
            warnings.Log(RelationalEventId.PendingModelChangesWarning)));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();

var dataProtectionKeyPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeyPath);
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
    .SetApplicationName("BugenceEditConsole");
builder.Services.AddScoped<ISensitiveDataProtector, SensitiveDataProtector>();
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<DomainRoutingOptions>(builder.Configuration.GetSection("DomainRouting"));
builder.Services.Configure<DomainObservabilityOptions>(builder.Configuration.GetSection("DomainObservability"));
builder.Services.Configure<DocumentTextOptions>(builder.Configuration.GetSection("DocumentText"));
builder.Services.Configure<CertificateProviderOptions>(builder.Configuration.GetSection("Certificates"));
builder.Services.Configure<FeatureFlagOptions>(builder.Configuration.GetSection("FeatureFlags"));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IPaymentGateway, PaymentGateway>();
builder.Services.AddScoped<IProjectDomainService, ProjectDomainService>();
builder.Services.AddScoped<IIisDomainBindingService, IisDomainBindingService>();
builder.Services.AddScoped<IIisProjectSiteService, IisProjectSiteService>();
builder.Services.AddScoped<IAnalyticsIngestService, AnalyticsService>();
builder.Services.AddScoped<IAnalyticsQueryService, AnalyticsService>();
builder.Services.AddScoped<IGoogleSearchConsoleService, GoogleSearchConsoleService>();
builder.Services.AddScoped<IDocumentTextService, DocumentTextService>();
builder.Services.AddScoped<RepeaterTemplateService>();
builder.Services.AddScoped<DebugPanelLogService>();
builder.Services.AddScoped<StubSslCertificateService>();
builder.Services.AddScoped<WebhookSslCertificateService>();
builder.Services.AddScoped<ILocalAcmeCertificateService, LocalAcmeCertificateService>();
builder.Services.AddScoped<ISslCertificateService>(sp =>
{
    var options = sp.GetRequiredService<IOptions<CertificateProviderOptions>>().Value;
    var logger = sp.GetRequiredService<ILoggerFactory>();
    if (string.Equals(options.Provider, "webhook", StringComparison.OrdinalIgnoreCase))
    {
        return sp.GetRequiredService<WebhookSslCertificateService>();
    }
    return sp.GetRequiredService<StubSslCertificateService>();
});
builder.Services.AddScoped<ICertificateProvisioningOrchestrator, CertificateProvisioningOrchestrator>();
builder.Services.AddScoped<IDomainVerificationService, DomainVerificationService>();
builder.Services.AddHostedService<DomainVerificationWorker>();
builder.Services.AddScoped<IDomainTelemetryService, DomainTelemetryService>();
builder.Services.AddScoped<IProjectPublishService, ProjectPublishService>();
builder.Services.AddScoped<IProjectSnapshotService, ProjectSnapshotService>();
builder.Services.AddScoped<IPreflightPublishService, PreflightPublishService>();
builder.Services.AddScoped<IDynamicVeArtifactService, DynamicVeArtifactService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDomainRouter, DomainRouter>();
builder.Services.AddHttpClient("certificate-webhook");
builder.Services.AddHttpClient("document-text");
builder.Services.AddHttpClient("gsc-api");
builder.Services.AddScoped<WorkflowExecutionService>();
builder.Services.AddScoped<MetaGraphClient>();
builder.Services.AddScoped<LeadMappingService>();
builder.Services.AddScoped<LeadDedupeService>();
builder.Services.AddScoped<TriggerRoutingService>();
builder.Services.AddScoped<FacebookLeadTriggerService>();
builder.Services.AddScoped<SocialFacebookMarketingService>();
builder.Services.AddScoped<IWhatsAppSender, WhatsAppWebhookSender>();
builder.Services.AddScoped<ICrmPushService, WebhookCrmPushService>();
builder.Services.AddScoped<ISessionNonceService, SessionNonceService>();
builder.Services.AddScoped<IGoogleOAuthRuntimeConfigService, GoogleOAuthRuntimeConfigService>();
builder.Services.AddSingleton<IPostConfigureOptions<OAuthOptions>, GoogleOAuthNamedOptionsSetup>();
builder.Services.AddHostedService<GoogleSearchConsoleSyncWorker>();
builder.Services.AddHostedService<SocialFacebookOpsWorker>();
builder.Services.AddHttpClient("meta-graph", client =>
{
    client.BaseAddress = new Uri("https://graph.facebook.com");
    client.Timeout = TimeSpan.FromSeconds(25);
});
builder.Services.AddHttpClient("whatsapp-api", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient("crm-webhook", client => client.Timeout = TimeSpan.FromSeconds(20));

var githubClientId = builder.Configuration["Authentication:GitHub:ClientId"];
var githubClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"];
if (!string.IsNullOrWhiteSpace(githubClientId) && !string.IsNullOrWhiteSpace(githubClientSecret))
{
    builder.Services.AddAuthentication()
        .AddOAuth("GitHub", options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.ClientId = githubClientId;
            options.ClientSecret = githubClientSecret;
            options.CallbackPath = "/signin-github";
            options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
            options.TokenEndpoint = "https://github.com/login/oauth/access_token";
            options.UserInformationEndpoint = "https://api.github.com/user";
            options.Scope.Add("user:email");
            options.SaveTokens = true;

            options.Events = new OAuthEvents
            {
                OnCreatingTicket = async context =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Bugence", "1.0"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

                    using var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                    response.EnsureSuccessStatusCode();

                    var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                    var root = payload.RootElement;
                    var login = root.TryGetProperty("login", out var loginProp) ? loginProp.GetString() : null;
                    var id = root.TryGetProperty("id", out var idProp) ? idProp.ToString() : null;
                    var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    var profileUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
                    var avatarUrl = root.TryGetProperty("avatar_url", out var avatarProp) ? avatarProp.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        context.Identity?.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, id));
                    }

                    if (!string.IsNullOrWhiteSpace(login))
                    {
                        context.Identity?.AddClaim(new System.Security.Claims.Claim("urn:github:login", login));
                        context.Identity?.AddClaim(new System.Security.Claims.Claim("github:username", login));
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        context.Identity?.AddClaim(new System.Security.Claims.Claim("urn:github:name", name));
                    }

                    if (!string.IsNullOrWhiteSpace(profileUrl))
                    {
                        context.Identity?.AddClaim(new System.Security.Claims.Claim("urn:github:url", profileUrl));
                        context.Identity?.AddClaim(new System.Security.Claims.Claim("github:url", profileUrl));
                    }

                    if (!string.IsNullOrWhiteSpace(avatarUrl))
                    {
                        context.Identity?.AddClaim(new System.Security.Claims.Claim("urn:github:avatar", avatarUrl));
                    }
                }
            };
        });
}

var facebookClientId = builder.Configuration["Authentication:Facebook:ClientId"];
var facebookClientSecret = builder.Configuration["Authentication:Facebook:ClientSecret"];
if (!string.IsNullOrWhiteSpace(facebookClientId) && !string.IsNullOrWhiteSpace(facebookClientSecret))
{
    builder.Services.AddAuthentication()
        .AddOAuth("Facebook", options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.ClientId = facebookClientId;
            options.ClientSecret = facebookClientSecret;
            options.CallbackPath = "/signin-facebook";
            options.AuthorizationEndpoint = "https://www.facebook.com/v19.0/dialog/oauth";
            options.TokenEndpoint = "https://graph.facebook.com/v19.0/oauth/access_token";
            options.UserInformationEndpoint = "https://graph.facebook.com/me?fields=id,name,email,picture";
            options.Scope.Add("email");
            options.Scope.Add("public_profile");
            options.SaveTokens = true;

            options.Events = new OAuthEvents
            {
                OnCreatingTicket = async context =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

                    using var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                    response.EnsureSuccessStatusCode();

                    var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                    var root = payload.RootElement;
                    var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
                    string? picture = null;
                    if (root.TryGetProperty("picture", out var pictureProp)
                        && pictureProp.TryGetProperty("data", out var dataProp)
                        && dataProp.TryGetProperty("url", out var urlProp))
                    {
                        picture = urlProp.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.NameIdentifier, id));
                        context.Identity?.AddClaim(new Claim("facebook:id", id));
                    }
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.Name, name));
                        context.Identity?.AddClaim(new Claim("facebook:name", name));
                    }
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.Email, email));
                        context.Identity?.AddClaim(new Claim("facebook:email", email));
                    }
                    if (!string.IsNullOrWhiteSpace(picture))
                    {
                        context.Identity?.AddClaim(new Claim("facebook:avatar", picture));
                    }
                }
            };
        });
}

var linkedInClientId = builder.Configuration["Authentication:LinkedIn:ClientId"];
var linkedInClientSecret = builder.Configuration["Authentication:LinkedIn:ClientSecret"];
if (!string.IsNullOrWhiteSpace(linkedInClientId) && !string.IsNullOrWhiteSpace(linkedInClientSecret))
{
    builder.Services.AddAuthentication()
        .AddOAuth("LinkedIn", options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.ClientId = linkedInClientId;
            options.ClientSecret = linkedInClientSecret;
            options.CallbackPath = "/signin-linkedin";
            options.AuthorizationEndpoint = "https://www.linkedin.com/oauth/v2/authorization";
            options.TokenEndpoint = "https://www.linkedin.com/oauth/v2/accessToken";
            options.UserInformationEndpoint = "https://api.linkedin.com/v2/userinfo";
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.SaveTokens = true;

            options.Events = new OAuthEvents
            {
                OnCreatingTicket = async context =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

                    using var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                    response.EnsureSuccessStatusCode();

                    var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                    var root = payload.RootElement;
                    var sub = root.TryGetProperty("sub", out var subProp) ? subProp.GetString() : null;
                    var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
                    var picture = root.TryGetProperty("picture", out var pictureProp) ? pictureProp.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(sub))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
                        context.Identity?.AddClaim(new Claim("linkedin:id", sub));
                    }
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.Name, name));
                        context.Identity?.AddClaim(new Claim("linkedin:name", name));
                    }
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.Email, email));
                        context.Identity?.AddClaim(new Claim("linkedin:email", email));
                    }
                    if (!string.IsNullOrWhiteSpace(picture))
                    {
                        context.Identity?.AddClaim(new Claim("linkedin:avatar", picture));
                    }
                }
            };
        });
}

var instagramClientId = builder.Configuration["Authentication:Instagram:ClientId"];
var instagramClientSecret = builder.Configuration["Authentication:Instagram:ClientSecret"];
if (!string.IsNullOrWhiteSpace(instagramClientId) && !string.IsNullOrWhiteSpace(instagramClientSecret))
{
    builder.Services.AddAuthentication()
        .AddOAuth("Instagram", options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.ClientId = instagramClientId;
            options.ClientSecret = instagramClientSecret;
            options.CallbackPath = "/signin-instagram";
            options.AuthorizationEndpoint = "https://api.instagram.com/oauth/authorize";
            options.TokenEndpoint = "https://api.instagram.com/oauth/access_token";
            options.UserInformationEndpoint = "https://graph.instagram.com/me?fields=id,username,account_type";
            options.Scope.Add("user_profile");
            options.SaveTokens = true;
            options.UsePkce = true;

            options.Events = new OAuthEvents
            {
                OnCreatingTicket = async context =>
                {
                    var endpoint = QueryHelpers.AddQueryString(
                        "https://graph.instagram.com/me",
                        new Dictionary<string, string?>()
                        {
                            ["fields"] = "id,username,account_type",
                            ["access_token"] = context.AccessToken
                        });
                    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    using var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                    response.EnsureSuccessStatusCode();

                    var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                    var root = payload.RootElement;
                    var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    var username = root.TryGetProperty("username", out var usernameProp) ? usernameProp.GetString() : null;
                    var accountType = root.TryGetProperty("account_type", out var accountTypeProp) ? accountTypeProp.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.NameIdentifier, id));
                        context.Identity?.AddClaim(new Claim("instagram:id", id));
                    }
                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.Name, username));
                        context.Identity?.AddClaim(new Claim("instagram:username", username));
                    }
                    if (!string.IsNullOrWhiteSpace(accountType))
                    {
                        context.Identity?.AddClaim(new Claim("instagram:account_type", accountType));
                    }
                }
            };
        });
}

var whatsAppClientId = builder.Configuration["Authentication:WhatsApp:ClientId"];
var whatsAppClientSecret = builder.Configuration["Authentication:WhatsApp:ClientSecret"];
if (!string.IsNullOrWhiteSpace(whatsAppClientId) && !string.IsNullOrWhiteSpace(whatsAppClientSecret))
{
    builder.Services.AddAuthentication()
        .AddOAuth("WhatsApp", options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.ClientId = whatsAppClientId;
            options.ClientSecret = whatsAppClientSecret;
            options.CallbackPath = "/signin-whatsapp";
            options.AuthorizationEndpoint = "https://www.facebook.com/v19.0/dialog/oauth";
            options.TokenEndpoint = "https://graph.facebook.com/v19.0/oauth/access_token";
            options.UserInformationEndpoint = "https://graph.facebook.com/me?fields=id,name";
            options.Scope.Add("whatsapp_business_management");
            options.Scope.Add("business_management");
            options.SaveTokens = true;

            options.Events = new OAuthEvents
            {
                OnCreatingTicket = async context =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

                    using var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                    response.EnsureSuccessStatusCode();

                    var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                    var root = payload.RootElement;
                    var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.NameIdentifier, id));
                        context.Identity?.AddClaim(new Claim("whatsapp:id", id));
                    }
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.Name, name));
                        context.Identity?.AddClaim(new Claim("whatsapp:name", name));
                    }
                }
            };
        });
}

builder.Services.AddAuthentication()
    .AddOAuth("Google", options =>
    {
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.ClientId = string.Empty;
        options.ClientSecret = string.Empty;
        options.CallbackPath = "/signin-google";
        options.AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        options.TokenEndpoint = "https://oauth2.googleapis.com/token";
        options.UserInformationEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
        options.Scope.Add("openid");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.Scope.Add("https://www.googleapis.com/auth/webmasters.readonly");
        options.SaveTokens = true;

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

                using var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();

                var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                var root = payload.RootElement;

                var sub = root.TryGetProperty("sub", out var subProp) ? subProp.GetString() : null;
                var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
                var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                var picture = root.TryGetProperty("picture", out var picProp) ? picProp.GetString() : null;

                if (!string.IsNullOrWhiteSpace(sub))
                {
                    context.Identity?.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, sub));
                }

                if (!string.IsNullOrWhiteSpace(email))
                {
                    context.Identity?.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, email));
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    context.Identity?.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, name));
                }

                if (!string.IsNullOrWhiteSpace(picture))
                {
                    context.Identity?.AddClaim(new System.Security.Claims.Claim("urn:google:avatar", picture));
                }
            }
        };
    });

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Bugence.EditConsole.Identity";
    options.LoginPath = "/Auth/Login";
    options.LogoutPath = "/Auth/Logout";
    options.AccessDeniedPath = "/Auth/Denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DeveloperToolsOnly", policy =>
        policy.RequireAssertion(context =>
        {
            var email = context.User.FindFirstValue(ClaimTypes.Email);
            return string.Equals(email, "admin@bugence.com", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(email, "admin@bugennce.com", StringComparison.OrdinalIgnoreCase);
        }));
});

builder.Services.AddScoped<IFileStorageService, FileSystemStorageService>();
builder.Services.AddScoped<IContentOrchestrator, ContentOrchestrator>();
builder.Services.AddSingleton<IStaticSiteManager, StaticSiteManager>();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddPageRoute("/Auth/Login", "login");
    options.Conventions.AddPageRoute("/Auth/Login", "app/login");
    options.Conventions.AddPageRoute("/Portal/Page", "page/{slug}");
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToFolder("/Auth");
    options.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddValidatorsFromAssemblyContaining<SectionUpsertFormValidator>();
builder.Services.AddSingleton<ITimelineStore, TimelineMemoryStore>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<ViteDevServerHostedService>();
}

var app = builder.Build();
var appStartedAtUtc = DateTimeOffset.UtcNow;

await DatabaseSeeder.SeedAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var isApiRequest = context.Request.Path.Value?.StartsWith("/api", StringComparison.OrdinalIgnoreCase) == true;
        if (!isApiRequest)
        {
            throw;
        }

        if (context.RequestAborted.IsCancellationRequested || ex is OperationCanceledException)
        {
            throw;
        }

        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ApiExceptionMiddleware");
        logger.LogError(ex, "Unhandled API exception for {Method} {Path}", context.Request.Method, context.Request.Path);

        if (context.Response.HasStarted)
        {
            throw;
        }

        var requestPath = context.Request.Path.Value;
        if (IsDomainPreflightPath(requestPath))
        {
            var routeDomainId = context.Request.RouteValues.TryGetValue("domainId", out var domainIdValue)
                ? Convert.ToString(domainIdValue)
                : null;
            var domainLabel = !string.IsNullOrWhiteSpace(routeDomainId)
                ? routeDomainId!
                : ExtractDomainIdFromPreflightPath(requestPath);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                domain = domainLabel,
                ok = false,
                checks = new[]
                {
                    new DomainPreflightCheck
                    {
                        key = "preflight_internal",
                        required = true,
                        ok = false,
                        detail = "Unable to evaluate preflight checks due to a server-side configuration error."
                    }
                }
            });
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            code = "INTERNAL_ERROR",
            message = "An unexpected server error occurred."
        });
    }
});

var staticSiteManager = app.Services.GetRequiredService<IStaticSiteManager>();
var activeSiteRoot = staticSiteManager.GetActiveSitePath();
var landingRoot = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "landing");
var siteRootToUse = !string.IsNullOrWhiteSpace(activeSiteRoot) && Directory.Exists(activeSiteRoot)
    ? activeSiteRoot
    : landingRoot;

app.Use(async (context, next) =>
{
    var requestPath = context.Request.Path.Value ?? string.Empty;
    if (requestPath.StartsWith("/.well-known/acme-challenge/", StringComparison.OrdinalIgnoreCase))
    {
        var token = requestPath["/.well-known/acme-challenge/".Length..];
        if (!string.IsNullOrWhiteSpace(token) && !token.Contains("..", StringComparison.Ordinal))
        {
            var challengePath = Path.Combine(
                app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
                ".well-known",
                "acme-challenge",
                token.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(challengePath))
            {
                context.Response.ContentType = "text/plain";
                await context.Response.SendFileAsync(challengePath);
                return;
            }
        }

        await next();
        return;
    }

    if (!context.Request.IsHttps)
    {
        var target = $"https://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect(target, permanent: true);
        return;
    }

    await next();
});
app.UseMiddleware<DebugPanelExceptionLoggingMiddleware>();
app.UseMiddleware<PublishedSiteMiddleware>();
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var isAssetPath = path.StartsWith("/Img/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Css/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Fonts/", StringComparison.OrdinalIgnoreCase);
    if (!isAssetPath)
    {
        await next();
        return;
    }

    var referer = context.Request.Headers.Referer.ToString();
    if (string.IsNullOrWhiteSpace(referer) ||
        !referer.Contains("handler=Preview", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
    {
        await next();
        return;
    }
    var query = QueryHelpers.ParseQuery(refererUri.Query);
    if (!query.TryGetValue("projectId", out var projectIdValues) ||
        !int.TryParse(projectIdValues.FirstOrDefault(), out var projectId))
    {
        await next();
        return;
    }

    using var scope = context.RequestServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var user = await userManager.GetUserAsync(context.User);
    if (user == null)
    {
        await next();
        return;
    }

    var projectQuery = db.UploadedProjects.AsNoTracking().Where(p => p.Id == projectId);
    if (user.CompanyId.HasValue)
    {
        projectQuery = projectQuery.Where(p => p.CompanyId == user.CompanyId);
    }
    else
    {
        projectQuery = projectQuery.Where(p => p.UserId == user.Id);
    }

    var project = await projectQuery.FirstOrDefaultAsync();
    if (project == null)
    {
        await next();
        return;
    }

    var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    var cleanPath = path.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString());
    var uploadsRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
    var assetPath = Path.Combine(uploadsRoot, cleanPath);
    if (!System.IO.File.Exists(assetPath))
    {
        var current = uploadsRoot;
        var parts = cleanPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (!Directory.Exists(current))
            {
                current = string.Empty;
                break;
            }
            var match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), part, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                current = string.Empty;
                break;
            }
            current = match;
        }
        if (!string.IsNullOrWhiteSpace(current) && System.IO.File.Exists(current))
        {
            assetPath = current;
        }
        else
        {
            var missingExt = Path.GetExtension(cleanPath).ToLowerInvariant();
            if (missingExt is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg")
            {
                var label = Path.GetFileName(cleanPath);
                var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"640\" height=\"360\" viewBox=\"0 0 640 360\"><rect width=\"640\" height=\"360\" fill=\"#e2e8f0\"/><text x=\"320\" y=\"180\" dominant-baseline=\"middle\" text-anchor=\"middle\" fill=\"#94a3b8\" font-size=\"24\" font-family=\"Arial\">{label}</text></svg>";
                context.Response.ContentType = "image/svg+xml";
                await context.Response.WriteAsync(svg);
                return;
            }
            if (missingExt == ".css")
            {
                context.Response.ContentType = "text/css";
                await context.Response.WriteAsync($"/* Missing asset: {cleanPath} */");
                return;
            }
            if (missingExt == ".js")
            {
                context.Response.ContentType = "text/javascript";
                await context.Response.WriteAsync($"/* Missing asset: {cleanPath} */");
                return;
            }
            await next();
            return;
        }
    }

    var ext = Path.GetExtension(assetPath).ToLowerInvariant();
    var contentType = ext switch
    {
        ".css" => "text/css",
        ".js" => "text/javascript",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".svg" => "image/svg+xml",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
    context.Response.ContentType = contentType;
    await context.Response.SendFileAsync(assetPath);
});
app.UseStaticFiles();

if (!string.IsNullOrWhiteSpace(siteRootToUse) && Directory.Exists(siteRootToUse))
{
    var defaultFileOptions = new DefaultFilesOptions
    {
        FileProvider = new PhysicalFileProvider(siteRootToUse),
        RequestPath = string.Empty
    };
    defaultFileOptions.DefaultFileNames.Clear();
    defaultFileOptions.DefaultFileNames.Add("index.html");
    app.UseDefaultFiles(defaultFileOptions);

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(siteRootToUse),
        RequestPath = string.Empty,
        ServeUnknownFileTypes = true
    });

    var scriptRoot = Path.Combine(siteRootToUse, "Script");
    if (Directory.Exists(scriptRoot))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(scriptRoot),
            RequestPath = string.Empty,
            ServeUnknownFileTypes = true
        });
    }
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/auth/session-state", async (
    HttpContext httpContext,
    UserManager<ApplicationUser> userManager,
    ISessionNonceService sessionNonceService) =>
{
    if (httpContext.User.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var cookieNonce = httpContext.User.FindFirstValue(sessionNonceService.ClaimType);
    var storedNonce = await sessionNonceService.GetCurrentNonceAsync(user);

    if (!string.IsNullOrWhiteSpace(storedNonce) && !string.Equals(cookieNonce, storedNonce, StringComparison.Ordinal))
    {
        return Results.Ok(new { valid = false, reason = "new_login_detected" });
    }

    return Results.Ok(new { valid = true });
}).RequireAuthorization();

app.MapGet("/api/analytics/collect.gif", async Task<IResult> (
    HttpContext httpContext,
    ApplicationDbContext db,
    IAnalyticsIngestService analyticsIngest,
    CancellationToken cancellationToken) =>
{
    var pidRaw = httpContext.Request.Query["pid"].FirstOrDefault();
    var hostRaw = httpContext.Request.Query["h"].FirstOrDefault();
    var pathRaw = httpContext.Request.Query["p"].FirstOrDefault();
    var refRaw = httpContext.Request.Query["r"].FirstOrDefault();
    var sidRaw = httpContext.Request.Query["sid"].FirstOrDefault();
    var ccRaw = httpContext.Request.Query["cc"].FirstOrDefault();
    var dtRaw = httpContext.Request.Query["dt"].FirstOrDefault();
    var cityRaw = httpContext.Request.Query["city"].FirstOrDefault();
    var langRaw = httpContext.Request.Query["lang"].FirstOrDefault();
    var titleRaw = httpContext.Request.Query["title"].FirstOrDefault();
    var lpRaw = httpContext.Request.Query["lp"].FirstOrDefault();
    var etMsRaw = httpContext.Request.Query["etms"].FirstOrDefault();
    var utmSourceRaw = httpContext.Request.Query["utm_source"].FirstOrDefault();
    var utmMediumRaw = httpContext.Request.Query["utm_medium"].FirstOrDefault();
    var utmCampaignRaw = httpContext.Request.Query["utm_campaign"].FirstOrDefault();
    var utmTermRaw = httpContext.Request.Query["utm_term"].FirstOrDefault();
    var utmContentRaw = httpContext.Request.Query["utm_content"].FirstOrDefault();
    var userAgent = httpContext.Request.Headers.UserAgent.ToString();

    if (int.TryParse(pidRaw, out var projectId) && projectId > 0 && !string.IsNullOrWhiteSpace(hostRaw))
    {
        var host = DomainUtilities.Normalize(hostRaw);
        var path = string.IsNullOrWhiteSpace(pathRaw) ? "/" : pathRaw!;
        if (path.Length > 1024)
        {
            path = path[..1024];
        }

        if (!string.IsNullOrWhiteSpace(host) &&
            !userAgent.Contains("bot", StringComparison.OrdinalIgnoreCase) &&
            !userAgent.Contains("crawler", StringComparison.OrdinalIgnoreCase) &&
            !userAgent.Contains("spider", StringComparison.OrdinalIgnoreCase))
        {
            var project = await db.UploadedProjects
                .AsNoTracking()
                .Where(p => p.Id == projectId)
                .Select(p => new { p.Id, p.UserId, p.CompanyId })
                .FirstOrDefaultAsync(cancellationToken);

            if (project is not null)
            {
                var sessionId = string.IsNullOrWhiteSpace(sidRaw) ? Guid.NewGuid().ToString("N") : sidRaw!;
                if (sessionId.Length > 128)
                {
                    sessionId = sessionId[..128];
                }

                var countryCode = httpContext.Request.Headers["CF-IPCountry"].ToString();
                if (string.IsNullOrWhiteSpace(countryCode))
                {
                    countryCode = httpContext.Request.Headers["X-Country"].ToString();
                }
                if (string.IsNullOrWhiteSpace(countryCode))
                {
                    countryCode = ccRaw ?? string.Empty;
                }

                var deviceType = string.IsNullOrWhiteSpace(dtRaw)
                    ? (userAgent.Contains("ipad", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("tablet", StringComparison.OrdinalIgnoreCase)
                        ? "tablet"
                        : (userAgent.Contains("mobi", StringComparison.OrdinalIgnoreCase) ||
                           userAgent.Contains("iphone", StringComparison.OrdinalIgnoreCase) ||
                           userAgent.Contains("android", StringComparison.OrdinalIgnoreCase)
                            ? "mobile"
                            : "desktop"))
                    : dtRaw;

                await analyticsIngest.TrackPageViewAsync(new AnalyticsIngestContext(
                    ProjectId: project.Id,
                    Host: host,
                    Path: path,
                    SessionId: sessionId,
                    CountryCode: countryCode,
                    DeviceType: deviceType,
                    City: cityRaw,
                    Language: langRaw,
                    ReferrerHost: Uri.TryCreate(refRaw, UriKind.Absolute, out var refUri) ? refUri.Host : null,
                    PageTitle: titleRaw,
                    LandingPath: lpRaw,
                    EngagementTimeMs: int.TryParse(etMsRaw, out var engagementMs) ? engagementMs : null,
                    UtmSource: utmSourceRaw,
                    UtmMedium: utmMediumRaw,
                    UtmCampaign: utmCampaignRaw,
                    UtmTerm: utmTermRaw,
                    UtmContent: utmContentRaw,
                    UserAgent: userAgent,
                    IsBot: false,
                    OccurredAtUtc: DateTime.UtcNow,
                    OwnerUserId: project.UserId,
                    CompanyId: project.CompanyId), cancellationToken);
            }
        }
    }

    httpContext.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
    var pixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
    return Results.File(pixel, "image/gif");
});

app.MapGet("/api/analytics/event.gif", async Task<IResult> (
    HttpContext httpContext,
    ApplicationDbContext db,
    IAnalyticsIngestService analyticsIngest,
    CancellationToken cancellationToken) =>
{
    var pidRaw = httpContext.Request.Query["pid"].FirstOrDefault();
    var hostRaw = httpContext.Request.Query["h"].FirstOrDefault();
    var pathRaw = httpContext.Request.Query["p"].FirstOrDefault();
    var refRaw = httpContext.Request.Query["r"].FirstOrDefault();
    var sidRaw = httpContext.Request.Query["sid"].FirstOrDefault();
    var ccRaw = httpContext.Request.Query["cc"].FirstOrDefault();
    var dtRaw = httpContext.Request.Query["dt"].FirstOrDefault();
    var etRaw = httpContext.Request.Query["et"].FirstOrDefault();
    var enRaw = httpContext.Request.Query["en"].FirstOrDefault();
    var langRaw = httpContext.Request.Query["lang"].FirstOrDefault();
    var titleRaw = httpContext.Request.Query["title"].FirstOrDefault();
    var utmSourceRaw = httpContext.Request.Query["utm_source"].FirstOrDefault();
    var utmMediumRaw = httpContext.Request.Query["utm_medium"].FirstOrDefault();
    var utmCampaignRaw = httpContext.Request.Query["utm_campaign"].FirstOrDefault();
    var utmTermRaw = httpContext.Request.Query["utm_term"].FirstOrDefault();
    var utmContentRaw = httpContext.Request.Query["utm_content"].FirstOrDefault();
    var userAgent = httpContext.Request.Headers.UserAgent.ToString();

    if (int.TryParse(pidRaw, out var projectId) &&
        projectId > 0 &&
        !string.IsNullOrWhiteSpace(hostRaw) &&
        !string.IsNullOrWhiteSpace(etRaw) &&
        !string.IsNullOrWhiteSpace(enRaw))
    {
        var host = DomainUtilities.Normalize(hostRaw);
        if (!string.IsNullOrWhiteSpace(host) &&
            !userAgent.Contains("bot", StringComparison.OrdinalIgnoreCase) &&
            !userAgent.Contains("crawler", StringComparison.OrdinalIgnoreCase) &&
            !userAgent.Contains("spider", StringComparison.OrdinalIgnoreCase))
        {
            var project = await db.UploadedProjects
                .AsNoTracking()
                .Where(p => p.Id == projectId)
                .Select(p => new { p.Id, p.UserId, p.CompanyId })
                .FirstOrDefaultAsync(cancellationToken);

            if (project is not null)
            {
                var sessionId = string.IsNullOrWhiteSpace(sidRaw) ? Guid.NewGuid().ToString("N") : sidRaw!;
                if (sessionId.Length > 128)
                {
                    sessionId = sessionId[..128];
                }

                var countryCode = httpContext.Request.Headers["CF-IPCountry"].ToString();
                if (string.IsNullOrWhiteSpace(countryCode))
                {
                    countryCode = httpContext.Request.Headers["X-Country"].ToString();
                }
                if (string.IsNullOrWhiteSpace(countryCode))
                {
                    countryCode = ccRaw ?? string.Empty;
                }

                var deviceType = string.IsNullOrWhiteSpace(dtRaw)
                    ? (userAgent.Contains("ipad", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("tablet", StringComparison.OrdinalIgnoreCase)
                        ? "tablet"
                        : (userAgent.Contains("mobi", StringComparison.OrdinalIgnoreCase) ||
                           userAgent.Contains("iphone", StringComparison.OrdinalIgnoreCase) ||
                           userAgent.Contains("android", StringComparison.OrdinalIgnoreCase)
                            ? "mobile"
                            : "desktop"))
                    : dtRaw;

                await analyticsIngest.TrackEventAsync(new AnalyticsEventIngestContext(
                    ProjectId: project.Id,
                    SessionId: sessionId,
                    EventType: etRaw!,
                    EventName: enRaw!,
                    Path: string.IsNullOrWhiteSpace(pathRaw) ? "/" : pathRaw!,
                    PageTitle: titleRaw,
                    CountryCode: countryCode,
                    DeviceType: deviceType,
                    Language: langRaw,
                    ReferrerHost: Uri.TryCreate(refRaw, UriKind.Absolute, out var refUri) ? refUri.Host : null,
                    MetadataJson: null,
                    UtmSource: utmSourceRaw,
                    UtmMedium: utmMediumRaw,
                    UtmCampaign: utmCampaignRaw,
                    UtmTerm: utmTermRaw,
                    UtmContent: utmContentRaw,
                    OccurredAtUtc: DateTime.UtcNow,
                    OwnerUserId: project.UserId,
                    CompanyId: project.CompanyId), cancellationToken);
            }
        }
    }

    httpContext.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
    var pixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
    return Results.File(pixel, "image/gif");
});

app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
    {
        await next();
        return;
    }

    var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
    if (path.StartsWith("/auth") ||
        path.StartsWith("/css") ||
        path.StartsWith("/js") ||
        path.StartsWith("/lib") ||
        path.StartsWith("/uploads") ||
        path.StartsWith("/favicon") ||
        path.StartsWith("/settings/billing") ||
        path.StartsWith("/api"))
    {
        await next();
        return;
    }

    var email = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
    if (!string.IsNullOrWhiteSpace(email) && email.Equals("admin@bugence.com", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var subscriptionService = context.RequestServices.GetRequiredService<ISubscriptionService>();
    var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
    var userId = userManager.GetUserId(context.User);
    if (!string.IsNullOrWhiteSpace(userId) && await subscriptionService.IsAccessLockedAsync(userId))
    {
        context.Response.Redirect("/Auth/TrialExpired");
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    await next();

    if (context.Response.StatusCode >= 400 ||
        !string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var path = context.Request.Path.Value ?? string.Empty;
    if (!path.StartsWith("/Tools", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    if (context.User.Identity?.IsAuthenticated != true)
    {
        return;
    }

    try
    {
        using var scope = context.RequestServices.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await userManager.GetUserAsync(context.User);
        if (user == null)
        {
            return;
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(db, user);
        await ToolDataSyncService.SyncAllToolTablesAsync(db, accessScope.OwnerUserId);
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ToolDataSyncMiddleware");
        logger.LogWarning(ex, "Automatic tools metadata sync failed for {Path}", path);
    }
});

var timeline = app.MapGroup("/api/timeline");

app.MapPost("/api/workflows/trigger", async (
    WorkflowTriggerRequest request,
    ApplicationDbContext db,
    WorkflowExecutionService executor,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var normalizedDguid = string.IsNullOrWhiteSpace(request.WorkflowDguid)
        ? string.Empty
        : request.WorkflowDguid.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

    if (request.WorkflowId == Guid.Empty && string.IsNullOrWhiteSpace(normalizedDguid))
    {
        return Results.BadRequest(new { success = false, message = "Missing workflow." });
    }

    Workflow? workflow = null;
    Workflow? workflowById = null;
    Workflow? workflowByDguid = null;

    if (request.WorkflowId != Guid.Empty)
    {
        workflowById = await db.Workflows.FirstOrDefaultAsync(w => w.Id == request.WorkflowId, cancellationToken);
    }

    if (!string.IsNullOrWhiteSpace(normalizedDguid))
    {
        // Load candidates locally so normalization is deterministic across providers.
        var dguidCandidates = await db.Workflows
            .Where(w => w.Dguid != null && w.Dguid != string.Empty)
            .ToListAsync(cancellationToken);
        workflowByDguid = dguidCandidates.FirstOrDefault(w =>
            w.Dguid != null &&
            w.Dguid.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant() == normalizedDguid);
    }

    // Resolution policy:
    // 1) If both provided and ID points to unavailable workflow, prefer DGUID workflow if available.
    // 2) If both available but different, prefer DGUID (latest binding identity for published pages).
    // 3) Fallback to whichever exists.
    static bool IsUnavailable(Workflow? w)
        => w == null
            || string.Equals(w.Status, "Archived", StringComparison.OrdinalIgnoreCase)
            || string.Equals(w.Status, "Deleted", StringComparison.OrdinalIgnoreCase);

    if (workflowByDguid != null && workflowById != null && workflowByDguid.Id != workflowById.Id)
    {
        workflow = !IsUnavailable(workflowByDguid) ? workflowByDguid : workflowById;
    }
    else if (!IsUnavailable(workflowByDguid) && IsUnavailable(workflowById))
    {
        workflow = workflowByDguid;
    }
    else
    {
        workflow = workflowById ?? workflowByDguid;
    }

    if (workflow == null)
    {
        return Results.NotFound(new { success = false, message = "Workflow not found." });
    }
    if (string.Equals(workflow.Status, "Archived", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(workflow.Status, "Deleted", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { success = false, message = "Workflow is not available." });
    }

    var fields = request.Fields ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(request.Email))
    {
        fields["email"] = request.Email;
    }

    var context = new WorkflowTriggerContext(
        Email: request.Email,
        Fields: fields,
        SourceUrl: request.SourceUrl ?? httpContext.Request.Headers.Referer.ToString(),
        ElementTag: request.ElementTag,
        ElementId: request.ElementId);

    var (success, error) = await executor.ExecuteAsync(workflow, context);
    if (!success)
    {
        return Results.BadRequest(new { success = false, message = error ?? "Workflow failed." });
    }

    return Results.Ok(new { success = true });
});

app.MapGet("/api/integrations/facebook/accounts", async (
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    await FacebookLeadTriggerSchemaService.EnsureSchemaAsync(db);
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var rows = await db.IntegrationConnections
        .AsNoTracking()
        .Where(x => x.OwnerUserId == user.Id && x.Provider == "facebook")
        .OrderByDescending(x => x.UpdatedAtUtc)
        .ToListAsync(cancellationToken);

    if (rows.Count == 0)
    {
        var claims = await userManager.GetClaimsAsync(user);
        var facebookId = claims.FirstOrDefault(c => c.Type == "facebook:id")?.Value;
        var facebookName = claims.FirstOrDefault(c => c.Type == "facebook:name")?.Value
            ?? claims.FirstOrDefault(c => c.Type == "facebook:email")?.Value;
        if (!string.IsNullOrWhiteSpace(facebookId))
        {
            var tokenScopes = await userManager.GetAuthenticationTokenAsync(user, "Facebook", "scope");
            var expires = await userManager.GetAuthenticationTokenAsync(user, "Facebook", "expires_at");
            DateTime? expiresAtUtc = DateTime.TryParse(expires, out var parsedExpiry) ? DateTime.SpecifyKind(parsedExpiry, DateTimeKind.Utc) : null;
            var status = "connected";
            if (expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTime.UtcNow) status = "disconnected";
            else if (expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTime.UtcNow.AddDays(7)) status = "expiring";

            var row = new IntegrationConnection
            {
                Provider = "facebook",
                DisplayName = string.IsNullOrWhiteSpace(facebookName) ? "Facebook Account" : facebookName,
                ExternalAccountId = facebookId,
                Status = status,
                ScopesJson = JsonSerializer.Serialize((tokenScopes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                ExpiresAtUtc = expiresAtUtc,
                OwnerUserId = user.Id,
                CompanyId = user.CompanyId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.IntegrationConnections.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            rows.Add(row);
        }
    }

    var data = rows.Select(row =>
    {
        string[] scopes = [];
        if (!string.IsNullOrWhiteSpace(row.ScopesJson))
        {
            try
            {
                scopes = JsonSerializer.Deserialize<string[]>(row.ScopesJson) ?? [];
            }
            catch
            {
                scopes = [];
            }
        }
        return new FacebookIntegrationAccountDto
        {
            ConnectionId = row.Id,
            Provider = row.Provider,
            DisplayName = row.DisplayName,
            ExternalAccountId = row.ExternalAccountId,
            Status = row.Status,
            Scopes = scopes,
            ExpiresAtUtc = row.ExpiresAtUtc,
            LastSyncedAtUtc = row.UpdatedAtUtc
        };
    });
    return Results.Ok(new { success = true, accounts = data });
}).RequireAuthorization().DisableAntiforgery();

app.MapGet("/api/integrations/facebook/assets", async (
    Guid connectionId,
    string type,
    string? parentId,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    MetaGraphClient graphClient,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    await FacebookLeadTriggerSchemaService.EnsureSchemaAsync(db);
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }
    var connection = await db.IntegrationConnections
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == connectionId && x.OwnerUserId == user.Id && x.Provider == "facebook", cancellationToken);
    if (connection == null)
    {
        return Results.NotFound(new { success = false, message = "Integration account not found." });
    }
    var normalizedType = (type ?? string.Empty).Trim().ToLowerInvariant();
    if (normalizedType is not ("ad_account" or "page" or "form"))
    {
        return Results.BadRequest(new { success = false, message = "Unsupported asset type." });
    }

    var assets = await graphClient.GetAssetsAsync(user, normalizedType, parentId, cancellationToken);

    var cacheRows = assets.Select(asset => new FacebookIntegrationAssetCache
    {
        ConnectionId = connection.Id,
        AssetType = asset.Type,
        ExternalId = asset.Id,
        Name = asset.Name,
        ParentExternalId = asset.ParentId,
        RawJson = JsonSerializer.Serialize(asset),
        FetchedAtUtc = DateTime.UtcNow
    }).ToList();

    if (cacheRows.Count > 0)
    {
        var externalIds = cacheRows.Select(x => x.ExternalId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oldRows = await db.FacebookIntegrationAssetCaches
            .Where(x => x.ConnectionId == connection.Id && x.AssetType == normalizedType && externalIds.Contains(x.ExternalId))
            .ToListAsync(cancellationToken);
        if (oldRows.Count > 0)
        {
            db.FacebookIntegrationAssetCaches.RemoveRange(oldRows);
        }
        db.FacebookIntegrationAssetCaches.AddRange(cacheRows);
        await db.SaveChangesAsync(cancellationToken);
    }

    return Results.Ok(new { success = true, assets });
}).RequireAuthorization().DisableAntiforgery();

app.MapGet("/api/workflows/{workflowId:guid}/triggers/{actionNodeId}", async (
    Guid workflowId,
    string actionNodeId,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    await WorkflowSchemaService.EnsureLegacyColumnsAsync(db);
    await FacebookLeadTriggerSchemaService.EnsureSchemaAsync(db);
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var workflow = await db.Workflows
        .AsNoTracking()
        .FirstOrDefaultAsync(w => w.Id == workflowId
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null), cancellationToken);
    if (workflow == null)
    {
        return Results.NotFound(new { success = false, message = "Workflow not found." });
    }

    var row = await db.WorkflowTriggerConfigs
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.WorkflowId == workflowId && x.ActionNodeId == actionNodeId && x.TriggerType == "facebook_lead", cancellationToken);

    if (row == null)
    {
        return Results.Ok(new
        {
            success = true,
            config = new FacebookLeadTriggerConfigDto
            {
                WorkflowId = workflowId,
                ActionNodeId = actionNodeId,
                TriggerType = "facebook_lead",
                Mode = "test",
                TriggerEvent = "leadgen.form.submit",
                MappingMode = "Lead Fields",
                MappingRules = [],
                Validation = new TriggerValidationConfigDto { RequireConsent = false, ReplayWindowMinutes = 10, RouteMissingContactToNeedsEnrichment = true },
                SplitFullName = true,
                NormalizePhone = true,
                ValidateEmail = true,
                DetectCountryFromPhone = true
            }
        });
    }

    List<LeadMappingRuleDto> rules = [];
    try
    {
        rules = JsonSerializer.Deserialize<List<LeadMappingRuleDto>>(row.MappingJson ?? "[]") ?? [];
    }
    catch { }

    TriggerValidationConfigDto validation;
    try
    {
        validation = JsonSerializer.Deserialize<TriggerValidationConfigDto>(row.ValidationConfigJson ?? "{}") ?? new TriggerValidationConfigDto();
    }
    catch
    {
        validation = new TriggerValidationConfigDto();
    }

    var dto = new FacebookLeadTriggerConfigDto
    {
        WorkflowId = row.WorkflowId,
        ActionNodeId = row.ActionNodeId,
        TriggerType = row.TriggerType,
        Mode = row.Mode,
        ConnectionId = row.ConnectionId,
        AdAccountId = row.AdAccountId,
        PageId = row.PageId,
        FormId = row.FormId,
        TriggerEvent = row.TriggerEvent,
        MappingMode = row.MappingMode,
        MappingRules = rules,
        Validation = validation,
        SplitFullName = true,
        NormalizePhone = true,
        ValidateEmail = true,
        DetectCountryFromPhone = true
    };

    return Results.Ok(new { success = true, config = dto });
}).RequireAuthorization().DisableAntiforgery();

app.MapPut("/api/workflows/{workflowId:guid}/triggers/{actionNodeId}", async (
    Guid workflowId,
    string actionNodeId,
    FacebookLeadTriggerConfigDto request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    await WorkflowSchemaService.EnsureLegacyColumnsAsync(db);
    await FacebookLeadTriggerSchemaService.EnsureSchemaAsync(db);
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var workflow = await db.Workflows
        .FirstOrDefaultAsync(w => w.Id == workflowId
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null), cancellationToken);
    if (workflow == null)
    {
        return Results.NotFound(new { success = false, message = "Workflow not found." });
    }

    var row = await db.WorkflowTriggerConfigs.FirstOrDefaultAsync(x =>
        x.WorkflowId == workflowId &&
        x.ActionNodeId == actionNodeId &&
        x.TriggerType == "facebook_lead", cancellationToken);
    if (row == null)
    {
        row = new WorkflowTriggerConfig
        {
            WorkflowId = workflowId,
            ActionNodeId = actionNodeId,
            TriggerType = "facebook_lead",
            OwnerUserId = user.Id,
            CompanyId = user.CompanyId
        };
        db.WorkflowTriggerConfigs.Add(row);
    }

    row.Mode = string.IsNullOrWhiteSpace(request.Mode) ? "test" : request.Mode.Trim().ToLowerInvariant();
    row.ConnectionId = request.ConnectionId;
    row.AdAccountId = request.AdAccountId;
    row.PageId = request.PageId;
    row.FormId = request.FormId;
    row.TriggerEvent = string.IsNullOrWhiteSpace(request.TriggerEvent) ? "leadgen.form.submit" : request.TriggerEvent.Trim();
    row.MappingMode = string.IsNullOrWhiteSpace(request.MappingMode) ? "Lead Fields" : request.MappingMode.Trim();
    row.MappingJson = JsonSerializer.Serialize(request.MappingRules ?? []);
    row.RequireConsent = request.Validation?.RequireConsent ?? false;
    row.ReplayWindowMinutes = request.Validation?.ReplayWindowMinutes > 0 ? request.Validation.ReplayWindowMinutes : 10;
    row.ValidationConfigJson = JsonSerializer.Serialize(request.Validation ?? new TriggerValidationConfigDto());
    row.OutputRoutingConfigJson = JsonSerializer.Serialize(new
    {
        primary = "Out",
        needs_enrichment = "Needs Enrichment",
        compliance_review = "Compliance Review",
        duplicate = "Out"
    });
    row.UpdatedAtUtc = DateTime.UtcNow;

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { success = true, configId = row.Id, updatedAt = row.UpdatedAtUtc });
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/api/workflows/{workflowId:guid}/triggers/{actionNodeId}/test-replay", async (
    Guid workflowId,
    string actionNodeId,
    FacebookLeadTriggerReplayRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    FacebookLeadTriggerService triggerService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    await WorkflowSchemaService.EnsureLegacyColumnsAsync(db);
    await FacebookLeadTriggerSchemaService.EnsureSchemaAsync(db);
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var workflow = await db.Workflows
        .FirstOrDefaultAsync(w => w.Id == workflowId
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null), cancellationToken);
    if (workflow == null)
    {
        return Results.NotFound(new { success = false, message = "Workflow not found." });
    }

    var config = await db.WorkflowTriggerConfigs.FirstOrDefaultAsync(x =>
        x.WorkflowId == workflowId &&
        x.ActionNodeId == actionNodeId &&
        x.TriggerType == "facebook_lead", cancellationToken);
    if (config == null)
    {
        config = new WorkflowTriggerConfig
        {
            WorkflowId = workflowId,
            ActionNodeId = actionNodeId,
            TriggerType = "facebook_lead",
            OwnerUserId = user.Id,
            CompanyId = user.CompanyId,
            Mode = "test",
            TriggerEvent = "leadgen.form.submit",
            MappingMode = "Lead Fields",
            MappingJson = "[]",
            ValidationConfigJson = "{}",
            OutputRoutingConfigJson = "{}"
        };
        db.WorkflowTriggerConfigs.Add(config);
        await db.SaveChangesAsync(cancellationToken);
    }

    var result = await triggerService.ExecuteTestReplayAsync(workflow, config, request, cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization().DisableAntiforgery();

app.MapGet("/api/workflows/triggers/presets", async (
    string? triggerType,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    await FacebookLeadTriggerSchemaService.EnsureSchemaAsync(db);
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }
    var normalizedType = string.IsNullOrWhiteSpace(triggerType) ? "facebook_lead" : triggerType.Trim().ToLowerInvariant();
    var rows = await db.WorkflowFieldMappingPresets
        .AsNoTracking()
        .Where(x => x.OwnerUserId == user.Id && x.TriggerType == normalizedType)
        .OrderByDescending(x => x.IsDefault)
        .ThenBy(x => x.Name)
        .ToListAsync(cancellationToken);
    var presets = rows.Select(x =>
    {
        IReadOnlyList<LeadMappingRuleDto> rules = [];
        try
        {
            rules = JsonSerializer.Deserialize<List<LeadMappingRuleDto>>(x.MappingJson ?? "[]") ?? [];
        }
        catch { }
        return new LeadMappingPresetDto
        {
            Id = x.Id,
            Name = x.Name,
            TriggerType = x.TriggerType,
            TargetEntity = x.TargetEntity,
            IsDefault = x.IsDefault,
            Rules = rules
        };
    });
    return Results.Ok(new { success = true, presets });
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/api/workflows/triggers/presets", async (
    LeadMappingPresetDto preset,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    await FacebookLeadTriggerSchemaService.EnsureSchemaAsync(db);
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }
    if (string.IsNullOrWhiteSpace(preset.Name))
    {
        return Results.BadRequest(new { success = false, message = "Preset name is required." });
    }

    var row = new WorkflowFieldMappingPreset
    {
        Name = preset.Name.Trim(),
        TriggerType = string.IsNullOrWhiteSpace(preset.TriggerType) ? "facebook_lead" : preset.TriggerType.Trim().ToLowerInvariant(),
        TargetEntity = string.IsNullOrWhiteSpace(preset.TargetEntity) ? "lead" : preset.TargetEntity.Trim().ToLowerInvariant(),
        MappingJson = JsonSerializer.Serialize(preset.Rules ?? []),
        IsDefault = preset.IsDefault,
        OwnerUserId = user.Id,
        CompanyId = user.CompanyId,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
    db.WorkflowFieldMappingPresets.Add(row);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { success = true, id = row.Id });
}).RequireAuthorization().DisableAntiforgery();

app.MapMethods("/api/webhooks/meta/leadgen", ["GET", "POST"], async (
    HttpContext httpContext,
    IOptions<FeatureFlagOptions> featureFlags,
    FacebookLeadTriggerService triggerService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("MetaLeadgenWebhook");

    if (HttpMethods.IsGet(httpContext.Request.Method))
    {
        var mode = httpContext.Request.Query["hub.mode"].ToString();
        var token = httpContext.Request.Query["hub.verify_token"].ToString();
        var challenge = httpContext.Request.Query["hub.challenge"].ToString();
        var configuredToken = httpContext.RequestServices.GetRequiredService<IConfiguration>()["Authentication:Facebook:WebhookVerifyToken"];
        if (string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(configuredToken) &&
            string.Equals(token, configuredToken, StringComparison.Ordinal))
        {
            return Results.Text(challenge);
        }
        return Results.BadRequest("Verification failed");
    }

    if (!featureFlags.Value.MetaWebhookLive)
    {
        return Results.Ok(new { success = true, mode = "disabled" });
    }

    using var reader = new StreamReader(httpContext.Request.Body);
    var payload = await reader.ReadToEndAsync(cancellationToken);
    if (string.IsNullOrWhiteSpace(payload))
    {
        return Results.Ok(new { success = true, message = "Empty payload." });
    }

    try
    {
        var root = JsonDocument.Parse(payload).RootElement;
        if (root.TryGetProperty("entry", out var entryNode) && entryNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entryNode.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changesNode) || changesNode.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (var change in changesNode.EnumerateArray())
                {
                    var field = change.TryGetProperty("field", out var fieldNode) ? fieldNode.GetString() : null;
                    if (!string.Equals(field, "leadgen", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var value = change.TryGetProperty("value", out var valueNode) ? valueNode : default;
                    var leadgenId = value.ValueKind != JsonValueKind.Undefined && value.TryGetProperty("leadgen_id", out var leadNode) ? leadNode.GetString() : null;
                    var workflowIdRaw = value.ValueKind != JsonValueKind.Undefined && value.TryGetProperty("workflow_id", out var workflowNode) ? workflowNode.GetString() : null;
                    var actionNodeId = value.ValueKind != JsonValueKind.Undefined && value.TryGetProperty("action_node_id", out var actionNode) ? actionNode.GetString() : null;
                    if (Guid.TryParse(workflowIdRaw, out var workflowId) && !string.IsNullOrWhiteSpace(actionNodeId) && !string.IsNullOrWhiteSpace(leadgenId))
                    {
                        await triggerService.HandleLiveWebhookAsync(workflowId, actionNodeId, leadgenId, cancellationToken);
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Meta webhook payload parse failed.");
    }

    return Results.Ok(new { success = true });
}).DisableAntiforgery();

app.MapMethods("/api/webhooks/meta/verify", ["GET"], async (HttpContext httpContext) =>
{
    var mode = httpContext.Request.Query["hub.mode"].ToString();
    var token = httpContext.Request.Query["hub.verify_token"].ToString();
    var challenge = httpContext.Request.Query["hub.challenge"].ToString();
    var configuredToken = httpContext.RequestServices.GetRequiredService<IConfiguration>()["Authentication:Facebook:WebhookVerifyToken"];
    if (string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(configuredToken) &&
        string.Equals(token, configuredToken, StringComparison.Ordinal))
    {
        return Results.Text(challenge);
    }
    return Results.BadRequest("Verification failed");
}).DisableAntiforgery();

app.MapSocialFacebookApi();

var dynamicVeApi = app.MapGroup("/api/dve").RequireAuthorization().DisableAntiforgery();

dynamicVeApi.MapPost("/session/start", async Task<IResult> (
    DynamicVeSessionStartRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (request.ProjectId <= 0)
    {
        return Results.BadRequest(new { success = false, message = "projectId is required." });
    }

    var project = await FindAccessibleProjectTrackedAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var pagePath = NormalizeDynamicVePath(request.PagePath);
    var config = await EnsureDynamicVeConfigAsync(db, project.Id, cancellationToken);
    var revision = await EnsureDynamicVeDraftRevisionAsync(db, project, pagePath, cancellationToken);
    var pages = await db.UploadedProjectFiles
        .AsNoTracking()
        .Where(f => f.UploadedProjectId == project.Id && !f.IsFolder)
        .Select(f => f.RelativePath.Replace("\\", "/"))
        .Where(p => p.EndsWith(".html") || p.EndsWith(".htm"))
        .OrderBy(p => p)
        .ToListAsync(cancellationToken);

    await AppendDynamicVeAuditAsync(db, project.Id, revision.Id, userManager, httpContext.User, "session-start", new { pagePath }, cancellationToken);
    return Results.Ok(new
    {
        success = true,
        projectId = project.Id,
        projectName = string.IsNullOrWhiteSpace(project.DisplayName) ? project.FolderName : project.DisplayName,
        pagePath,
        revisionId = revision.Id,
        config = new
        {
            config.Mode,
            config.RuntimePolicy,
            config.FeatureEnabled,
            config.DraftRevisionId,
            config.StagingRevisionId,
            config.LiveRevisionId
        },
        pages
    });
});

dynamicVeApi.MapGet("/page/load", async Task<IResult> (
    int projectId,
    string? path,
    string? env,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<FeatureFlagOptions> featureFlags,
    IWebHostEnvironment webHostEnvironment,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (!featureFlags.Value.DynamicVeWorkflowV2)
    {
        return Results.NotFound(new { success = false, message = "Dynamic VE workflow v2 is disabled." });
    }

    await DynamicVeSchemaService.EnsureActionBindingColumnsAsync(db);

    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, projectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var pagePath = NormalizeDynamicVePath(path);
    var webRoot = webHostEnvironment.WebRootPath ?? Path.Combine(webHostEnvironment.ContentRootPath, "wwwroot");
    var fullPath = Path.GetFullPath(Path.Combine(webRoot, "Uploads", project.FolderName, pagePath.Replace("/", Path.DirectorySeparatorChar.ToString())));
    var projectRoot = Path.GetFullPath(Path.Combine(webRoot, "Uploads", project.FolderName));
    if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
    {
        return Results.BadRequest(new { success = false, message = "Page not found." });
    }

    var html = await File.ReadAllTextAsync(fullPath, cancellationToken);
    var normalizedEnv = string.IsNullOrWhiteSpace(env) ? "draft" : env.Trim().ToLowerInvariant();
    var revision = await db.DynamicVePageRevisions
        .AsNoTracking()
        .Where(r => r.UploadedProjectId == project.Id && r.PagePath == pagePath && r.Environment == normalizedEnv)
        .OrderByDescending(r => r.CreatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    List<object> rules = new();
    List<object> textPatches = new();
    List<object> sections = new();
    List<object> bindings = new();
    List<object> elementMaps = new();
    List<object> resolutionSummary = new();
    if (revision != null)
    {
        var mapRows = await db.DynamicVeElementMaps
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .ToListAsync(cancellationToken);
        var maps = mapRows.ToDictionary(x => x.ElementKey, StringComparer.OrdinalIgnoreCase);

        static IReadOnlyList<string> ParseFallbackSelectors(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        elementMaps = mapRows
            .Select(x => new
            {
                x.ElementKey,
                primarySelector = x.PrimarySelector,
                fallbackSelectors = ParseFallbackSelectors(x.FallbackSelectorsJson),
                x.FingerprintHash,
                x.AnchorHash,
                x.Confidence,
                x.LastResolvedSelector,
                x.LastResolvedAtUtc
            } as object)
            .ToList();
        resolutionSummary = mapRows
            .Select(x => new
            {
                x.ElementKey,
                resolved = !string.IsNullOrWhiteSpace(x.LastResolvedSelector),
                confidence = x.Confidence,
                x.LastResolvedSelector,
                x.LastResolvedAtUtc
            } as object)
            .ToList();

        var ruleRows = await db.DynamicVePatchRules
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        rules = ruleRows.Select(x => new
        {
            x.Id,
            x.ElementKey,
            selector = maps.ContainsKey(x.ElementKey) ? maps[x.ElementKey].PrimarySelector : string.Empty,
            fallbackSelectors = maps.ContainsKey(x.ElementKey) ? ParseFallbackSelectors(maps[x.ElementKey].FallbackSelectorsJson) : Array.Empty<string>(),
            x.Breakpoint,
            x.State,
            x.Property,
            x.Value,
            x.Priority
        } as object).ToList();

        var textRows = await db.DynamicVeTextPatches
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        textPatches = textRows.Select(x => new
        {
            x.Id,
            x.ElementKey,
            selector = maps.ContainsKey(x.ElementKey) ? maps[x.ElementKey].PrimarySelector : string.Empty,
            fallbackSelectors = maps.ContainsKey(x.ElementKey) ? ParseFallbackSelectors(maps[x.ElementKey].FallbackSelectorsJson) : Array.Empty<string>(),
            x.TextMode,
            x.Content
        } as object).ToList();

        var sectionRows = await db.DynamicVeSectionInstances
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        sections = sectionRows.Select(x => new
        {
            x.Id,
            x.TemplateId,
            x.InsertMode,
            x.TargetElementKey,
            selector = maps.ContainsKey(x.TargetElementKey) ? maps[x.TargetElementKey].PrimarySelector : string.Empty,
            fallbackSelectors = maps.ContainsKey(x.TargetElementKey) ? ParseFallbackSelectors(maps[x.TargetElementKey].FallbackSelectorsJson) : Array.Empty<string>(),
            x.MarkupJson,
            x.CssJson,
            x.JsJson
        } as object).ToList();

        var bindingRows = await db.DynamicVeActionBindings
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        bindings = bindingRows.Select(x => new
        {
            x.Id,
            x.ElementKey,
            selector = maps.ContainsKey(x.ElementKey) ? maps[x.ElementKey].PrimarySelector : string.Empty,
            fallbackSelectors = maps.ContainsKey(x.ElementKey) ? ParseFallbackSelectors(maps[x.ElementKey].FallbackSelectorsJson) : Array.Empty<string>(),
            x.ActionType,
            x.WorkflowId,
            x.WorkflowDguid,
            x.WorkflowNameSnapshot,
            x.NavigateUrl,
            x.TriggerEvent,
            x.ValidationJson,
            x.BehaviorJson
        } as object).ToList();
    }

    var previewUrl = $"/Uploads/{Uri.EscapeDataString(project.FolderName)}/{string.Join("/", pagePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString))}";
    return Results.Ok(new
    {
        success = true,
        pagePath,
        previewUrl,
        html,
        revisionId = revision?.Id,
        overlay = new
        {
            elementMaps,
            rules,
            textPatches,
            sections,
            bindings
        },
        resolutionSummary
    });
});

dynamicVeApi.MapGet("/workflows/list", async Task<IResult> (
    int projectId,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<FeatureFlagOptions> featureFlags,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (!featureFlags.Value.DynamicVeWorkflowV2)
    {
        return Results.NotFound(new { success = false, message = "Dynamic VE workflow v2 is disabled." });
    }

    await DynamicVeSchemaService.EnsureActionBindingColumnsAsync(db);

    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, projectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var workflows = await db.Workflows
        .AsNoTracking()
        .Where(w => w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null)
            && w.Status != "Archived"
            && w.Status != "Deleted")
        .OrderBy(w => w.Name)
        .Select(w => new
        {
            id = w.Id,
            dguid = w.Dguid,
            name = w.Caption ?? w.Name,
            status = w.Status,
            inActive = w.InActive,
            updatedAtUtc = w.UpdatedAtUtc,
            triggerType = w.TriggerType
        })
        .ToListAsync(cancellationToken);

    if (workflows.Count == 0)
    {
        workflows = await db.Workflows
            .AsNoTracking()
            .Where(w => w.Status != "Archived" && w.Status != "Deleted")
            .OrderBy(w => w.Name)
            .Select(w => new
            {
                id = w.Id,
                dguid = w.Dguid,
                name = w.Caption ?? w.Name,
                status = w.Status,
                inActive = w.InActive,
                updatedAtUtc = w.UpdatedAtUtc,
                triggerType = w.TriggerType
            })
            .ToListAsync(cancellationToken);
    }

    return Results.Ok(new { success = true, workflows });
});

dynamicVeApi.MapPost("/element/resolve", async Task<IResult> (
    DynamicVeResolveElementRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectTrackedAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var pagePath = NormalizeDynamicVePath(request.PagePath);
    var revision = await EnsureDynamicVeDraftRevisionAsync(db, project, pagePath, cancellationToken);
    var selector = (request.Selector ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(selector))
    {
        return Results.BadRequest(new { success = false, message = "Selector is required." });
    }

    var elementKey = string.IsNullOrWhiteSpace(request.ElementKey)
        ? BuildDynamicVeElementKey(pagePath, selector)
        : request.ElementKey.Trim();
    await UpsertDynamicVeElementMapAsync(
        db,
        revision.Id,
        elementKey,
        selector,
        request.FallbackSelectors,
        request.FingerprintHash,
        request.AnchorHash,
        cancellationToken);
    await db.SaveChangesAsync(cancellationToken);
    var map = await db.DynamicVeElementMaps.AsNoTracking()
        .FirstOrDefaultAsync(x => x.RevisionId == revision.Id && x.ElementKey == elementKey, cancellationToken);
    return Results.Ok(new
    {
        success = true,
        elementKey,
        selector,
        confidence = map?.Confidence ?? 0.5m,
        lastResolvedSelector = map?.LastResolvedSelector ?? selector
    });
});

dynamicVeApi.MapPost("/edit/text", async Task<IResult> (
    DynamicVeEditTextRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectTrackedAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var pagePath = NormalizeDynamicVePath(request.PagePath);
    var revision = await EnsureDynamicVeDraftRevisionAsync(db, project, pagePath, cancellationToken);
    var selector = (request.Selector ?? string.Empty).Trim();
    var elementKey = string.IsNullOrWhiteSpace(request.ElementKey) ? BuildDynamicVeElementKey(pagePath, selector) : request.ElementKey.Trim();
    if (string.IsNullOrWhiteSpace(elementKey))
    {
        return Results.BadRequest(new { success = false, message = "Element key is required." });
    }

    await UpsertDynamicVeElementMapAsync(db, revision.Id, elementKey, selector, null, null, null, cancellationToken);
    var textMode = string.IsNullOrWhiteSpace(request.TextMode) ? "plain" : request.TextMode.Trim().ToLowerInvariant();
    var existingPatch = await db.DynamicVeTextPatches
        .FirstOrDefaultAsync(x => x.RevisionId == revision.Id && x.ElementKey == elementKey && x.TextMode == textMode, cancellationToken);
    if (existingPatch == null)
    {
        db.DynamicVeTextPatches.Add(new DynamicVeTextPatch
        {
            RevisionId = revision.Id,
            ElementKey = elementKey,
            TextMode = textMode,
            Content = request.Content ?? string.Empty
        });
    }
    else
    {
        existingPatch.Content = request.Content ?? string.Empty;
    }
    await db.SaveChangesAsync(cancellationToken);

    await AppendDynamicVeAuditAsync(db, project.Id, revision.Id, userManager, httpContext.User, "text-set", new { pagePath, elementKey }, cancellationToken);
    return Results.Ok(new { success = true, revisionId = revision.Id, elementKey });
});

dynamicVeApi.MapPost("/edit/style", async Task<IResult> (
    DynamicVeEditStyleRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectTrackedAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var pagePath = NormalizeDynamicVePath(request.PagePath);
    var revision = await EnsureDynamicVeDraftRevisionAsync(db, project, pagePath, cancellationToken);
    var selector = (request.Selector ?? string.Empty).Trim();
    var elementKey = string.IsNullOrWhiteSpace(request.ElementKey) ? BuildDynamicVeElementKey(pagePath, selector) : request.ElementKey.Trim();
    if (string.IsNullOrWhiteSpace(elementKey) || string.IsNullOrWhiteSpace(request.Property))
    {
        return Results.BadRequest(new { success = false, message = "Element key and property are required." });
    }

    await UpsertDynamicVeElementMapAsync(db, revision.Id, elementKey, selector, null, null, null, cancellationToken);
    var breakpoint = string.IsNullOrWhiteSpace(request.Breakpoint) ? "desktop" : request.Breakpoint.Trim().ToLowerInvariant();
    var stateName = string.IsNullOrWhiteSpace(request.State) ? "base" : request.State.Trim().ToLowerInvariant();
    var propertyName = request.Property.Trim();
    var existingRule = await db.DynamicVePatchRules.FirstOrDefaultAsync(
        x => x.RevisionId == revision.Id
            && x.ElementKey == elementKey
            && x.Breakpoint == breakpoint
            && x.State == stateName
            && x.Property == propertyName,
        cancellationToken);
    if (existingRule == null)
    {
        db.DynamicVePatchRules.Add(new DynamicVePatchRule
        {
            RevisionId = revision.Id,
            ElementKey = elementKey,
            RuleType = "style",
            Breakpoint = breakpoint,
            State = stateName,
            Property = propertyName,
            Value = request.Value ?? string.Empty,
            Priority = 0
        });
    }
    else
    {
        existingRule.Value = request.Value ?? string.Empty;
    }
    await db.SaveChangesAsync(cancellationToken);

    await AppendDynamicVeAuditAsync(db, project.Id, revision.Id, userManager, httpContext.User, "style-set", new { pagePath, elementKey, request.Property }, cancellationToken);
    return Results.Ok(new { success = true, revisionId = revision.Id, elementKey });
});

dynamicVeApi.MapPost("/section/insert", async Task<IResult> (
    DynamicVeSectionInsertRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectTrackedAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    if (string.IsNullOrWhiteSpace(request.Markup))
    {
        return Results.BadRequest(new { success = false, message = "Markup is required." });
    }

    var pagePath = NormalizeDynamicVePath(request.PagePath);
    var revision = await EnsureDynamicVeDraftRevisionAsync(db, project, pagePath, cancellationToken);
    db.DynamicVeSectionInstances.Add(new DynamicVeSectionInstance
    {
        RevisionId = revision.Id,
        TemplateId = string.IsNullOrWhiteSpace(request.TemplateId) ? "custom" : request.TemplateId.Trim(),
        InsertMode = string.IsNullOrWhiteSpace(request.InsertMode) ? "after" : request.InsertMode.Trim().ToLowerInvariant(),
        TargetElementKey = request.TargetElementKey ?? string.Empty,
        MarkupJson = JsonSerializer.Serialize(new { html = request.Markup }),
        CssJson = JsonSerializer.Serialize(new { css = request.Css ?? string.Empty }),
        JsJson = JsonSerializer.Serialize(new { js = request.Js ?? string.Empty })
    });
    await db.SaveChangesAsync(cancellationToken);

    await AppendDynamicVeAuditAsync(db, project.Id, revision.Id, userManager, httpContext.User, "section-insert", new { pagePath, request.TemplateId }, cancellationToken);
    return Results.Ok(new { success = true, revisionId = revision.Id });
});

dynamicVeApi.MapPost("/bind/action", async Task<IResult> (
    DynamicVeBindActionRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<FeatureFlagOptions> featureFlags,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (!featureFlags.Value.DynamicVeWorkflowV2)
    {
        return Results.NotFound(new { success = false, message = "Dynamic VE workflow v2 is disabled." });
    }

    await DynamicVeSchemaService.EnsureActionBindingColumnsAsync(db);

    var project = await FindAccessibleProjectTrackedAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var pagePath = NormalizeDynamicVePath(request.PagePath);
    var revision = await EnsureDynamicVeDraftRevisionAsync(db, project, pagePath, cancellationToken);
    if (string.IsNullOrWhiteSpace(request.ElementKey))
    {
        return Results.BadRequest(new { success = false, message = "Element key is required." });
    }

    var elementKey = request.ElementKey.Trim();
    var actionType = string.IsNullOrWhiteSpace(request.ActionType) ? "navigate" : request.ActionType.Trim().ToLowerInvariant();
    var definition = DynamicVeActionRegistry.Get(actionType);
    if (definition == null)
    {
        return Results.BadRequest(new { success = false, message = $"Unsupported action type '{actionType}'." });
    }

    var triggerEvent = NormalizeTriggerEvent(request.TriggerEvent);
    if (!definition.AllowedTriggerEvents.Contains(triggerEvent, StringComparer.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { success = false, message = $"Trigger event '{triggerEvent}' is not allowed for action '{actionType}'." });
    }

    if ((actionType is "navigate" or "hybrid") && string.IsNullOrWhiteSpace(request.NavigateUrl))
    {
        return Results.BadRequest(new { success = false, message = "Navigate URL required for navigate/hybrid actions." });
    }

    Workflow? resolvedWorkflow = null;
    string normalizedWorkflowDguid = string.Empty;
    if (actionType is "workflow" or "hybrid")
    {
        resolvedWorkflow = await ResolveWorkflowByIdOrDguidAsync(db, request.WorkflowId, request.WorkflowDguid, cancellationToken);
        if (resolvedWorkflow == null)
        {
            return Results.BadRequest(new { success = false, message = "Valid workflow is required for workflow/hybrid actions." });
        }
        normalizedWorkflowDguid = NormalizeDguid(resolvedWorkflow.Dguid);
    }

    var validationJson = request.Validation.HasValue ? request.Validation.Value.GetRawText() : "{}";
    var behaviorJson = request.Behavior.HasValue ? request.Behavior.Value.GetRawText() : "{}";

    var existingBinding = await db.DynamicVeActionBindings
        .FirstOrDefaultAsync(x => x.RevisionId == revision.Id && x.ElementKey == elementKey, cancellationToken);
    if (existingBinding == null)
    {
        db.DynamicVeActionBindings.Add(new DynamicVeActionBinding
        {
            RevisionId = revision.Id,
            ElementKey = elementKey,
            ActionType = actionType,
            WorkflowId = resolvedWorkflow?.Id,
            WorkflowDguid = string.IsNullOrWhiteSpace(normalizedWorkflowDguid) ? null : normalizedWorkflowDguid,
            WorkflowNameSnapshot = resolvedWorkflow == null ? null : (resolvedWorkflow.Caption ?? resolvedWorkflow.Name),
            NavigateUrl = request.NavigateUrl,
            TriggerEvent = triggerEvent,
            ValidationJson = validationJson,
            BehaviorJson = behaviorJson
        });
    }
    else
    {
        existingBinding.ActionType = actionType;
        existingBinding.WorkflowId = resolvedWorkflow?.Id;
        existingBinding.WorkflowDguid = string.IsNullOrWhiteSpace(normalizedWorkflowDguid) ? null : normalizedWorkflowDguid;
        existingBinding.WorkflowNameSnapshot = resolvedWorkflow == null ? null : (resolvedWorkflow.Caption ?? resolvedWorkflow.Name);
        existingBinding.NavigateUrl = request.NavigateUrl;
        existingBinding.TriggerEvent = triggerEvent;
        existingBinding.ValidationJson = validationJson;
        existingBinding.BehaviorJson = behaviorJson;
    }
    await db.SaveChangesAsync(cancellationToken);

    await AppendDynamicVeAuditAsync(db, project.Id, revision.Id, userManager, httpContext.User, "action-bind", new { pagePath, request.ElementKey, request.ActionType }, cancellationToken);
    return Results.Ok(new
    {
        success = true,
        revisionId = revision.Id,
        binding = new
        {
            elementKey,
            actionType,
            workflowId = resolvedWorkflow?.Id,
            workflowDguid = string.IsNullOrWhiteSpace(normalizedWorkflowDguid) ? null : normalizedWorkflowDguid,
            workflowName = resolvedWorkflow == null ? null : (resolvedWorkflow.Caption ?? resolvedWorkflow.Name),
            navigateUrl = request.NavigateUrl,
            triggerEvent,
            validationJson
        }
    });
});

dynamicVeApi.MapPost("/bind/test", async Task<IResult> (
    DynamicVeBindTestRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    WorkflowExecutionService workflowExecutionService,
    ISensitiveDataProtector protector,
    IOptions<FeatureFlagOptions> featureFlags,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (!featureFlags.Value.DynamicVeWorkflowV2)
    {
        return Results.NotFound(new { success = false, message = "Dynamic VE workflow v2 is disabled." });
    }

    await DynamicVeSchemaService.EnsureActionBindingColumnsAsync(db);

    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }
    if (string.IsNullOrWhiteSpace(request.ElementKey))
    {
        return Results.BadRequest(new { success = false, message = "Element key is required." });
    }

    var pagePath = NormalizeDynamicVePath(request.PagePath);
    var revision = await db.DynamicVePageRevisions
        .AsNoTracking()
        .Where(r => r.UploadedProjectId == project.Id && r.PagePath == pagePath && r.Environment == "draft")
        .OrderByDescending(r => r.CreatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);
    if (revision == null)
    {
        return Results.BadRequest(new { success = false, message = "No revision found for page." });
    }

    var binding = await db.DynamicVeActionBindings
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.RevisionId == revision.Id && x.ElementKey == request.ElementKey, cancellationToken);
    if (binding == null)
    {
        return Results.NotFound(new { success = false, message = "No binding found for selected element." });
    }

    var traceId = Guid.NewGuid().ToString("N");
    var actionType = binding.ActionType?.ToLowerInvariant() ?? "navigate";
    var errors = new List<string>();
    var warnings = new List<string>();
    var executionPath = new List<string>();
    string? resolvedRecipient = null;
    string smtpProfileUsed = "app-fallback";
    object? workflowMeta = null;
    bool ok = true;

    if (actionType is "workflow" or "hybrid")
    {
        var workflow = await ResolveWorkflowByIdOrDguidAsync(db, binding.WorkflowId, binding.WorkflowDguid, cancellationToken);
        if (workflow == null)
        {
            ok = false;
            errors.Add("Workflow is missing for this binding.");
        }
        else
        {
            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in request.MockFields ?? new Dictionary<string, string?>())
            {
                fields[pair.Key] = pair.Value;
            }
            foreach (var pair in request.Overrides?.Fields ?? new Dictionary<string, string?>())
            {
                fields[pair.Key] = pair.Value;
            }
            var testEmail = request.Overrides?.Email ?? request.MockEmail;
            var context = new WorkflowTriggerContext(
                Email: testEmail,
                Fields: fields,
                SourceUrl: $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/{pagePath}",
                ElementTag: "button",
                ElementId: request.ElementKey);

            resolvedRecipient = await ResolveWorkflowRecipientPreviewAsync(db, workflow, context, cancellationToken);
            if (string.IsNullOrWhiteSpace(resolvedRecipient))
            {
                warnings.Add("Recipient could not be resolved from current test payload.");
            }

            smtpProfileUsed = await ResolveSmtpProfilePreviewNameAsync(db, protector, workflow, cancellationToken);
            workflowMeta = new
            {
                id = workflow.Id,
                dguid = workflow.Dguid,
                name = workflow.Caption ?? workflow.Name,
                status = workflow.Status,
                triggerType = workflow.TriggerType
            };

            var (success, error) = await workflowExecutionService.ExecuteAsync(workflow, context);
            if (!success)
            {
                ok = false;
                errors.Add(error ?? "Workflow execution failed.");
            }
            executionPath.Add("workflow");
        }
    }

    if (actionType is "navigate" or "hybrid")
    {
        if (string.IsNullOrWhiteSpace(binding.NavigateUrl))
        {
            ok = false;
            errors.Add("Navigate URL is missing.");
        }
        executionPath.Add("navigate");
    }

    await AppendDynamicVeAuditAsync(
        db,
        project.Id,
        revision.Id,
        userManager,
        httpContext.User,
        "binding-test",
        new
        {
            traceId,
            elementKey = request.ElementKey,
            actionType = binding.ActionType,
            ok,
            errors
        },
        cancellationToken);

    return Results.Ok(new
    {
        success = true,
        ok,
        traceId,
        executionPath,
        errors,
        warnings,
        workflow = workflowMeta,
        resolvedRecipient = MaskEmail(resolvedRecipient),
        smtpProfileUsed
    });
});

dynamicVeApi.MapPost("/trace", async Task<IResult> (
    JsonElement payload,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var projectId = payload.TryGetProperty("projectId", out var p) && p.TryGetInt32(out var parsedProjectId)
        ? parsedProjectId
        : 0;
    var revisionId = payload.TryGetProperty("revisionId", out var r) && r.TryGetInt64(out var parsedRevisionId)
        ? parsedRevisionId
        : (long?)null;
    if (projectId <= 0)
    {
        return Results.BadRequest(new { success = false, message = "projectId is required." });
    }

    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, projectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var sampleBucket = Random.Shared.Next(0, 100);
    if (sampleBucket < 25)
    {
        await AppendDynamicVeAuditAsync(
            db,
            projectId,
            revisionId,
            userManager,
            httpContext.User,
            "runtime-trace",
            payload,
            cancellationToken);
    }

    return Results.Ok(new { success = true });
});

dynamicVeApi.MapPost("/save-draft", async Task<IResult> (
    DynamicVeSaveDraftRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectTrackedAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var pagePath = NormalizeDynamicVePath(request.PagePath);
    var revision = await EnsureDynamicVeDraftRevisionAsync(db, project, pagePath, cancellationToken);

    if (request.Patch.HasValue)
    {
        var patch = request.Patch.Value;
        var selector = patch.TryGetProperty("selector", out var s) ? s.GetString() : string.Empty;
        var elementKey = BuildDynamicVeElementKey(pagePath, selector ?? "body");
        await UpsertDynamicVeElementMapAsync(db, revision.Id, elementKey, selector ?? "body", null, null, null, cancellationToken);

        if (patch.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String)
        {
            db.DynamicVeTextPatches.Add(new DynamicVeTextPatch
            {
                RevisionId = revision.Id,
                ElementKey = elementKey,
                TextMode = "plain",
                Content = textNode.GetString() ?? string.Empty
            });
        }

        if (patch.TryGetProperty("styles", out var styleNode) && styleNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in styleNode.EnumerateObject())
            {
                db.DynamicVePatchRules.Add(new DynamicVePatchRule
                {
                    RevisionId = revision.Id,
                    ElementKey = elementKey,
                    RuleType = "style",
                    Breakpoint = "desktop",
                    State = "base",
                    Property = prop.Name,
                    Value = prop.Value.ToString()
                });
            }
        }
    }

    await db.SaveChangesAsync(cancellationToken);
    await AppendDynamicVeAuditAsync(db, project.Id, revision.Id, userManager, httpContext.User, "save-draft", new { pagePath }, cancellationToken);
    return Results.Ok(new { success = true, revisionId = revision.Id });
});

dynamicVeApi.MapPost("/ops/append", async Task<IResult> (
    JsonElement payload,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<FeatureFlagOptions> featureFlags,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (!featureFlags.Value.DynamicVeInspectorProV1)
    {
        return Results.NotFound(new { success = false, message = "Ops append is disabled." });
    }

    var projectId = payload.TryGetProperty("projectId", out var p) && p.TryGetInt32(out var parsedProjectId)
        ? parsedProjectId
        : 0;
    if (projectId <= 0)
    {
        return Results.BadRequest(new { success = false, message = "projectId is required." });
    }

    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, projectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    long? revisionId = null;
    if (payload.TryGetProperty("revisionId", out var rid) && rid.TryGetInt64(out var parsedRid))
    {
        revisionId = parsedRid;
    }

    await AppendDynamicVeAuditAsync(
        db,
        projectId,
        revisionId,
        userManager,
        httpContext.User,
        "ops-append",
        payload,
        cancellationToken);

    return Results.Ok(new { success = true });
});

dynamicVeApi.MapPost("/preflight", async Task<IResult> (
    DynamicVePublishRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    IWebHostEnvironment environment,
    IPreflightPublishService preflightService,
    CancellationToken cancellationToken) =>
{
    await DynamicVeSchemaService.EnsureActionBindingColumnsAsync(db);

    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var pagePath = NormalizeDynamicVePath(request.PagePath);
    var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    var projectRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
    var fullPath = Path.Combine(projectRoot, pagePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
    var html = File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath, cancellationToken) : string.Empty;

    var preflight = preflightService.Evaluate(new PreflightPublishRequest
    {
        Project = project,
        FilePath = pagePath,
        HtmlBefore = html,
        HtmlAfter = html,
        WebRootPath = webRoot,
        ProjectRootPath = projectRoot
    });

    var revision = await db.DynamicVePageRevisions
        .AsNoTracking()
        .Where(r => r.UploadedProjectId == project.Id && r.PagePath == pagePath && r.Environment == "draft")
        .OrderByDescending(r => r.CreatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);
    var diagnostics = new
    {
        unresolvedElements = 0,
        unresolvedBindings = 0,
        invalidSections = 0,
        lowConfidenceCount = 0,
        confidenceBuckets = new { high = 0, medium = 0, low = 0 }
    };
    if (revision != null)
    {
        var mapRows = await db.DynamicVeElementMaps
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .ToListAsync(cancellationToken);
        var mapLookup = mapRows.ToDictionary(x => x.ElementKey, StringComparer.OrdinalIgnoreCase);
        var bindings = await db.DynamicVeActionBindings
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .ToListAsync(cancellationToken);
        var sections = await db.DynamicVeSectionInstances
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .ToListAsync(cancellationToken);

        var unresolvedElements = mapRows.Count(x => string.IsNullOrWhiteSpace(x.LastResolvedSelector));
        var unresolvedBindings = bindings.Count(x => !mapLookup.TryGetValue(x.ElementKey, out var map) || string.IsNullOrWhiteSpace(map.LastResolvedSelector));
        var invalidSections = sections.Count(x => !string.IsNullOrWhiteSpace(x.TargetElementKey) && (!mapLookup.TryGetValue(x.TargetElementKey, out var map) || string.IsNullOrWhiteSpace(map.LastResolvedSelector)));
        var lowConfidenceCount = mapRows.Count(x => x.Confidence < 0.70m);
        var high = mapRows.Count(x => x.Confidence >= 0.85m);
        var medium = mapRows.Count(x => x.Confidence >= 0.70m && x.Confidence < 0.85m);
        var low = mapRows.Count(x => x.Confidence < 0.70m);
        diagnostics = new
        {
            unresolvedElements,
            unresolvedBindings,
            invalidSections,
            lowConfidenceCount,
            confidenceBuckets = new { high, medium, low }
        };
    }

    var finalWarnings = preflight.Warnings != null
        ? new List<string>(preflight.Warnings)
        : new List<string>();
    if (diagnostics.unresolvedElements > 0)
    {
        finalWarnings.Add($"Dynamic VE unresolved mapped elements: {diagnostics.unresolvedElements}.");
    }
    if (diagnostics.unresolvedBindings > 0)
    {
        finalWarnings.Add($"Dynamic VE unresolved bindings: {diagnostics.unresolvedBindings}.");
    }
    if (diagnostics.invalidSections > 0)
    {
        finalWarnings.Add($"Dynamic VE invalid section targets: {diagnostics.invalidSections}.");
    }
    if (diagnostics.lowConfidenceCount > 0)
    {
        finalWarnings.Add($"Dynamic VE low-confidence selectors: {diagnostics.lowConfidenceCount}.");
    }

    return Results.Ok(new
    {
        success = true,
        safe = preflight.Safe,
        score = preflight.Score,
        blockers = preflight.Blockers,
        warnings = finalWarnings,
        diffSummary = preflight.DiffSummary,
        dveDiagnostics = diagnostics
    });
});

dynamicVeApi.MapPost("/publish", async Task<IResult> (
    DynamicVePublishRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    IWebHostEnvironment environment,
    IPreflightPublishService preflightService,
    IDynamicVeArtifactService artifactService,
    IProjectSnapshotService snapshotService,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectTrackedAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var pagePath = NormalizeDynamicVePath(request.PagePath);
    var draftRevision = await EnsureDynamicVeDraftRevisionAsync(db, project, pagePath, cancellationToken);
    var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    var projectRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
    var fullPath = Path.Combine(projectRoot, pagePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
    var html = File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath, cancellationToken) : string.Empty;
    var preflight = preflightService.Evaluate(new PreflightPublishRequest
    {
        Project = project,
        FilePath = pagePath,
        HtmlBefore = html,
        HtmlAfter = html,
        WebRootPath = webRoot,
        ProjectRootPath = projectRoot
    });

    if (!preflight.Safe && !request.OverrideRisk)
    {
        return Results.Conflict(new
        {
            success = false,
            message = "Preflight blocked publish.",
            safe = preflight.Safe,
            score = preflight.Score,
            blockers = preflight.Blockers,
            warnings = preflight.Warnings
        });
    }

    var liveRevision = await CreateDynamicVeLiveRevisionFromDraftAsync(db, draftRevision, cancellationToken);
    var config = await EnsureDynamicVeConfigAsync(db, project.Id, cancellationToken);
    config.LiveRevisionId = liveRevision.Id;
    config.UpdatedAtUtc = DateTime.UtcNow;

    var artifact = await artifactService.BuildOverlayArtifactAsync(project, liveRevision, db, cancellationToken);
    if (File.Exists(fullPath))
    {
        var updatedHtml = AttachDynamicVeRuntime(
            File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath, cancellationToken) : string.Empty,
            artifact.ArtifactPath,
            project.Id,
            liveRevision.Id);
        await File.WriteAllTextAsync(fullPath, updatedHtml, cancellationToken);
    }
    db.DynamicVePublishArtifacts.Add(new DynamicVePublishArtifact
    {
        RevisionId = liveRevision.Id,
        ArtifactType = "overlay-package",
        ArtifactPath = artifact.ArtifactPath,
        Checksum = artifact.Checksum,
        PublishedAtUtc = DateTime.UtcNow
    });
    var currentUser = await userManager.GetUserAsync(httpContext.User);
    var snapshot = await snapshotService.CreateSnapshotAsync(
        project,
        "live",
        "dynamic-ve-publish",
        currentUser?.Id,
        isSuccessful: true,
        versionLabel: $"dve-live-{DateTime.UtcNow:yyyyMMddHHmmss}",
        cancellationToken: cancellationToken);
    if (snapshot.Snapshot != null)
    {
        liveRevision.BaseSnapshotId = snapshot.Snapshot.Id;
    }

    await db.SaveChangesAsync(cancellationToken);
    await AppendDynamicVeAuditAsync(db, project.Id, liveRevision.Id, userManager, httpContext.User, "publish", new { pagePath, artifact.ArtifactPath }, cancellationToken);

    return Results.Ok(new
    {
        success = true,
        revisionId = liveRevision.Id,
        draftRevisionId = draftRevision.Id,
        artifactPath = artifact.ArtifactPath,
        checksum = artifact.Checksum,
        preflight = new { preflight.Safe, preflight.Score, preflight.Blockers, preflight.Warnings },
        snapshotId = snapshot.Snapshot?.Id
    });
});

dynamicVeApi.MapPost("/rollback", async Task<IResult> (
    DynamicVeRollbackRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    IDynamicVeArtifactService artifactService,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectTrackedAsync(db, userManager, httpContext.User, request.ProjectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var config = await EnsureDynamicVeConfigAsync(db, project.Id, cancellationToken);
    long? targetRevisionId = request.RevisionId;
    if (!targetRevisionId.HasValue)
    {
        targetRevisionId = await db.DynamicVePageRevisions
            .Where(r => r.UploadedProjectId == project.Id && r.Status == "published")
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip(1)
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    if (!targetRevisionId.HasValue)
    {
        return Results.BadRequest(new { success = false, message = "No rollback target available." });
    }

    var target = await db.DynamicVePageRevisions.FirstOrDefaultAsync(r => r.Id == targetRevisionId.Value && r.UploadedProjectId == project.Id, cancellationToken);
    if (target == null)
    {
        return Results.NotFound(new { success = false, message = "Revision not found." });
    }

    config.LiveRevisionId = target.Id;
    config.UpdatedAtUtc = DateTime.UtcNow;
    target.Status = "published";
    target.Environment = "live";
    var artifact = await artifactService.BuildOverlayArtifactAsync(project, target, db, cancellationToken);
    var rollbackPath = NormalizeDynamicVePath(request.PagePath ?? target.PagePath);
    var webRoot = (httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath ?? Path.Combine(httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "wwwroot"));
    var rollbackFull = Path.Combine(webRoot, "Uploads", project.FolderName, rollbackPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
    if (File.Exists(rollbackFull))
    {
        var rollbackHtml = await File.ReadAllTextAsync(rollbackFull, cancellationToken);
        rollbackHtml = AttachDynamicVeRuntime(rollbackHtml, artifact.ArtifactPath, project.Id, target.Id);
        await File.WriteAllTextAsync(rollbackFull, rollbackHtml, cancellationToken);
    }
    db.DynamicVePublishArtifacts.Add(new DynamicVePublishArtifact
    {
        RevisionId = target.Id,
        ArtifactType = "overlay-package",
        ArtifactPath = artifact.ArtifactPath,
        Checksum = artifact.Checksum,
        PublishedAtUtc = DateTime.UtcNow
    });

    await db.SaveChangesAsync(cancellationToken);
    await AppendDynamicVeAuditAsync(db, project.Id, target.Id, userManager, httpContext.User, "rollback", new { targetRevisionId = target.Id }, cancellationToken);
    return Results.Ok(new { success = true, revisionId = target.Id, artifactPath = artifact.ArtifactPath });
});

dynamicVeApi.MapGet("/diff", async Task<IResult> (
    long fromRevisionId,
    long toRevisionId,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var from = await db.DynamicVePageRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == fromRevisionId, cancellationToken);
    var to = await db.DynamicVePageRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == toRevisionId, cancellationToken);
    if (from == null || to == null || from.UploadedProjectId != to.UploadedProjectId)
    {
        return Results.NotFound(new { success = false, message = "Revisions not found." });
    }

    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, from.UploadedProjectId, cancellationToken);
    if (project == null)
    {
        return Results.Forbid();
    }

    var fromRules = await db.DynamicVePatchRules.AsNoTracking().Where(x => x.RevisionId == from.Id).Select(x => $"{x.ElementKey}|{x.Property}|{x.Value}|{x.Breakpoint}|{x.State}").ToListAsync(cancellationToken);
    var toRules = await db.DynamicVePatchRules.AsNoTracking().Where(x => x.RevisionId == to.Id).Select(x => $"{x.ElementKey}|{x.Property}|{x.Value}|{x.Breakpoint}|{x.State}").ToListAsync(cancellationToken);
    var added = toRules.Except(fromRules, StringComparer.OrdinalIgnoreCase).ToList();
    var removed = fromRules.Except(toRules, StringComparer.OrdinalIgnoreCase).ToList();

    return Results.Ok(new
    {
        success = true,
        from = from.Id,
        to = to.Id,
        added,
        removed,
        changed = new string[0]
    });
});

dynamicVeApi.MapGet("/telemetry/project", async Task<IResult> (
    int projectId,
    string? window,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, projectId, cancellationToken);
    if (project == null)
    {
        return Results.NotFound(new { success = false, message = "Project not found." });
    }

    var days = string.Equals(window, "30d", StringComparison.OrdinalIgnoreCase) ? 30 : 7;
    var since = DateTime.UtcNow.AddDays(-days);
    var logs = await db.DynamicVeAuditLogs
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId && x.AtUtc >= since)
        .ToListAsync(cancellationToken);

    return Results.Ok(new
    {
        success = true,
        projectId,
        window = $"{days}d",
        actions = logs.GroupBy(x => x.Action).Select(g => new { action = g.Key, count = g.Count() }).OrderByDescending(x => x.count),
        total = logs.Count
    });
});


// GET /api/timeline/events?pageId=GUID (polling)
timeline.MapGet("events", (Guid pageId, ITimelineStore store) =>
{
    var list = store.GetRecent(pageId, 50);
    return Results.Json(list);
});


// GET /api/timeline/stream?pageId=GUID (Server‑Sent Events)
timeline.MapGet("stream", async (HttpContext http, Guid pageId, ITimelineStore store, CancellationToken ct) =>
{
    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers["X-Accel-Buffering"] = "no"; // for reverse proxies


    // Warm greeting so UI shows immediate state
    store.Publish(pageId, new TimelineEvent(
    Timestamp: DateTimeOffset.UtcNow,
    Type: "connected",
    Message: "Timeline connected"));


    await foreach (var evt in store.Subscribe(pageId, ct))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(evt);
        await http.Response.WriteAsync($"data: {json}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
});

var projectDomainsApi = app.MapGroup("/api/projects/{projectId:int}/domains")
    .RequireAuthorization();

app.MapPost("/api/projects/{projectId:int}/publish", async Task<IResult> (
    int projectId,
    HttpContext httpContext,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IProjectPublishService projectPublish,
    IProjectSnapshotService snapshotService,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, projectId, cancellationToken);
    if (project is null)
    {
        return Results.NotFound();
    }

    var result = await projectPublish.PublishAsync(projectId, "domains", cancellationToken);
    if (!result.Success)
    {
        return Results.BadRequest(new { message = result.Message });
    }

    var currentUser = await userManager.GetUserAsync(httpContext.User);
    var snapshot = await snapshotService.CreateSnapshotAsync(
        project,
        "live",
        "domains-publish",
        currentUser?.Id,
        isSuccessful: true,
        versionLabel: $"domains-{DateTime.UtcNow:yyyyMMddHHmmss}",
        cancellationToken: cancellationToken);

    return Results.Ok(new
    {
        success = true,
        message = result.Message,
        publishedAtUtc = result.PublishedAtUtc,
        publishStoragePath = result.PublishStoragePath,
        snapshotId = snapshot.Snapshot?.Id
    });
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/api/projects/{projectId:int}/restore-last", async Task<IResult> (
    int projectId,
    HttpContext httpContext,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IWebHostEnvironment env,
    IProjectPublishService projectPublish,
    IProjectSnapshotService snapshotService,
    IDomainVerificationService verificationService,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectTrackedAsync(db, userManager, httpContext.User, projectId, cancellationToken);
    if (project is null)
    {
        return Results.NotFound();
    }

    var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    var restoreZip = Path.Combine(env.ContentRootPath, "App_Data", "restore", projectId.ToString(), "restore.zip");
    if (!File.Exists(restoreZip))
    {
        return Results.BadRequest(new { message = "No restore backup found for this project." });
    }

    var uploadRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
    if (Directory.Exists(uploadRoot))
    {
        Directory.Delete(uploadRoot, recursive: true);
    }
    Directory.CreateDirectory(uploadRoot);

    System.IO.Compression.ZipFile.ExtractToDirectory(restoreZip, uploadRoot, overwriteFiles: true);
    await RebuildProjectFileIndexAsync(db, projectId, uploadRoot, cancellationToken);

    db.PreviousDeploys.Add(new PreviousDeploy
    {
        UploadedProjectId = projectId,
        PayloadJson = JsonSerializer.Serialize(new { eventType = "restore", source = "api", artifact = new { backup = "restore.zip" } }),
        StoredAtUtc = DateTime.UtcNow
    });
    await db.SaveChangesAsync(cancellationToken);

    var publishResult = await projectPublish.PublishAsync(projectId, "restore", cancellationToken);
    var currentUser = await userManager.GetUserAsync(httpContext.User);
    var snapshot = await snapshotService.CreateSnapshotAsync(
        project,
        "live",
        "restore-api",
        currentUser?.Id,
        isSuccessful: publishResult.Success,
        versionLabel: $"restore-{DateTime.UtcNow:yyyyMMddHHmmss}",
        cancellationToken: cancellationToken);
    var domains = await db.ProjectDomains
        .Where(d => d.UploadedProjectId == projectId && d.DomainType == ProjectDomainType.Custom)
        .Select(d => d.Id)
        .ToListAsync(cancellationToken);
    foreach (var domainId in domains)
    {
        try { await verificationService.VerifyDomainAsync(domainId, cancellationToken); }
        catch { }
    }

    return Results.Ok(new
    {
        success = publishResult.Success,
        message = publishResult.Success ? "Backup restored and published." : publishResult.Message,
        publishedAtUtc = publishResult.PublishedAtUtc,
        snapshotId = snapshot.Snapshot?.Id
    });
}).RequireAuthorization().DisableAntiforgery();

projectDomainsApi.MapGet("", async Task<IResult> (
    int projectId,
    HttpContext httpContext,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IProjectDomainService domainService,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, projectId, cancellationToken);
    if (project is null)
    {
        return Results.NotFound();
    }

    var domains = await domainService.GetDomainsAsync(projectId, cancellationToken);
    var primary = domains.FirstOrDefault(d => d.DomainType == ProjectDomainType.Primary);
    var custom = domains.Where(d => d.DomainType == ProjectDomainType.Custom).ToList();

    var effectivePrimary = custom
        .Where(d => d.Status == DomainStatus.Connected && d.SslStatus == DomainSslStatus.Active)
        .OrderByDescending(d => d.UpdatedAtUtc)
        .Select(d => d.DomainName)
        .FirstOrDefault()
        ?? primary?.DomainName;

    return Results.Ok(new
    {
        project = new
        {
            id = project.Id,
            name = string.IsNullOrWhiteSpace(project.DisplayName) ? project.FolderName : project.DisplayName,
            slug = project.Slug,
            primaryDomain = primary?.DomainName,
            effectivePrimaryDomain = effectivePrimary
        },
        domains = custom.Select(d => MapDomainDto(d, project))
    });
});

projectDomainsApi.MapPost("", async Task<IResult> (
    int projectId,
    HttpContext httpContext,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    [FromBody] DomainRequest request,
    IProjectDomainService domainService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Domain))
    {
        return Results.BadRequest(new { message = "Domain name is required." });
    }

    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, projectId, cancellationToken);
    if (project is null)
    {
        return Results.NotFound();
    }

    try
    {
        var domain = await domainService.AddCustomDomainAsync(projectId, request.Domain, cancellationToken);
        return Results.Ok(new { domain = MapDomainDto(domain, project) });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
}).DisableAntiforgery();

projectDomainsApi.MapDelete("{domainId:guid}", async Task<IResult> (
    int projectId,
    Guid domainId,
    HttpContext httpContext,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IProjectDomainService domainService,
    CancellationToken cancellationToken) =>
{
    var project = await FindAccessibleProjectAsync(db, userManager, httpContext.User, projectId, cancellationToken);
    if (project is null)
    {
        return Results.NotFound();
    }

    try
    {
        await domainService.RemoveDomainAsync(projectId, domainId, cancellationToken);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

var domainRecordsApi = app.MapGroup("/api/domains")
    .RequireAuthorization();

domainRecordsApi.MapGet("{domainId:guid}/dns-records", async Task<IResult> (
    Guid domainId,
    HttpContext httpContext,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IProjectDomainService domainService,
    CancellationToken cancellationToken) =>
{
    var domain = await db.ProjectDomains.AsNoTracking()
        .Include(d => d.Project)
        .FirstOrDefaultAsync(d => d.Id == domainId, cancellationToken);
    if (domain is null)
    {
        return Results.NotFound();
    }
    if (domain.Project is null)
    {
        return Results.NotFound();
    }

    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var hasAccess = user.CompanyId.HasValue
        ? domain.Project.CompanyId == user.CompanyId
        : string.Equals(domain.Project.UserId, user.Id, StringComparison.Ordinal);
    if (!hasAccess)
    {
        return Results.NotFound();
    }

    var records = await domainService.GetDnsRecordsAsync(domainId, cancellationToken);
    return Results.Ok(new
    {
        domain = domain.DomainName,
        records = records.Select(r => new
        {
            id = r.Id,
            type = r.RecordType,
            name = r.Name,
            value = r.Value,
            required = r.IsRequired,
            satisfied = r.IsSatisfied,
            purpose = r.Purpose,
            lastCheckedAtUtc = r.LastCheckedAtUtc
        })
    });
});

domainRecordsApi.MapPost("{domainId:guid}/refresh", async Task<IResult> (
    Guid domainId,
    HttpContext httpContext,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IDomainVerificationService verificationService,
    CancellationToken cancellationToken) =>
{
    var domainLookup = await db.ProjectDomains.AsNoTracking()
        .Include(d => d.Project)
        .FirstOrDefaultAsync(d => d.Id == domainId, cancellationToken);
    if (domainLookup is null)
    {
        return Results.NotFound();
    }
    if (domainLookup.Project is null)
    {
        return Results.NotFound();
    }

    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var hasAccess = user.CompanyId.HasValue
        ? domainLookup.Project.CompanyId == user.CompanyId
        : string.Equals(domainLookup.Project.UserId, user.Id, StringComparison.Ordinal);
    if (!hasAccess)
    {
        return Results.NotFound();
    }

    var domain = await verificationService.VerifyDomainAsync(domainId, cancellationToken);
    if (domain is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new { domain = MapDomainDto(domain, domainLookup.Project) });
}).DisableAntiforgery();

domainRecordsApi.MapPost("{domainId:guid}/preflight", async Task<IResult> (
    Guid domainId,
    HttpContext httpContext,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ILogger<Program> logger,
    IWebHostEnvironment environment,
    IOptions<CertificateProviderOptions> certificateOptions,
    IOptions<DomainRoutingOptions> routingOptions,
    CancellationToken cancellationToken) =>
{
    var domainLabel = domainId.ToString();
    var requestPath = httpContext.Request.Path.ToString();
    try
    {
        var domain = await db.ProjectDomains.AsNoTracking()
            .Include(d => d.Project)
            .Include(d => d.DnsRecords)
            .FirstOrDefaultAsync(d => d.Id == domainId, cancellationToken);
        if (domain is null)
        {
            return Results.NotFound();
        }
        if (domain.Project is null)
        {
            return Results.NotFound();
        }

        domainLabel = domain.DomainName;

        var user = await userManager.GetUserAsync(httpContext.User);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var hasAccess = user.CompanyId.HasValue
            ? domain.Project.CompanyId == user.CompanyId
            : string.Equals(domain.Project.UserId, user.Id, StringComparison.Ordinal);
        if (!hasAccess)
        {
            return Results.NotFound();
        }

        if (domain.Status == DomainStatus.Connected && domain.SslStatus == DomainSslStatus.Active)
        {
            return Results.Ok(new
            {
                domain = domain.DomainName,
                ok = true,
                checks = new[]
                {
                    new DomainPreflightCheck
                    {
                        key = "already_connected",
                        required = true,
                        ok = true,
                        detail = "Domain is already connected and SSL is active."
                    }
                }
            });
        }

        var certificateConfig = certificateOptions.Value ?? new CertificateProviderOptions();
        var routingConfig = routingOptions.Value ?? new DomainRoutingOptions();
        var checks = BuildDomainPreflightChecks(domain, environment, certificateConfig, routingConfig);
        var ok = checks.All(c => c.Ok || !c.Required);
        return Results.Ok(new
        {
            domain = domain.DomainName,
            ok,
            checks
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Domain preflight request failed for {DomainId} at {Path}", domainId, requestPath);
        return Results.Ok(new
        {
            domain = domainLabel,
            ok = false,
            checks = new[]
            {
                new DomainPreflightCheck
                {
                    key = "preflight_internal",
                    required = true,
                    ok = false,
                    detail = "Unable to evaluate preflight checks due to a server-side configuration error."
                }
            }
        });
    }
}).DisableAntiforgery();

domainRecordsApi.MapGet("{domainId:guid}/history", async Task<IResult> (
    Guid domainId,
    HttpContext httpContext,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    var domain = await db.ProjectDomains.AsNoTracking()
        .Include(d => d.Project)
        .FirstOrDefaultAsync(d => d.Id == domainId, cancellationToken);
    if (domain is null)
    {
        return Results.NotFound();
    }
    if (domain.Project is null)
    {
        return Results.NotFound();
    }

    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var hasAccess = user.CompanyId.HasValue
        ? domain.Project.CompanyId == user.CompanyId
        : string.Equals(domain.Project.UserId, user.Id, StringComparison.Ordinal);
    if (!hasAccess)
    {
        return Results.NotFound();
    }

    var entries = await db.DomainVerificationLogs
        .AsNoTracking()
        .Where(l => l.ProjectDomainId == domainId)
        .OrderByDescending(l => l.CheckedAtUtc)
        .Take(20)
        .ToListAsync(cancellationToken);

    return Results.Ok(new
    {
        domain = domain.DomainName,
        history = entries.Select(l => new
        {
            status = l.Status.ToString(),
            sslStatus = l.SslStatus.ToString(),
            recordsSatisfied = l.AllRecordsSatisfied,
            message = l.Message,
            checkedAtUtc = l.CheckedAtUtc
        })
    });
});

var domainTelemetryApi = app.MapGroup("/api/domain-telemetry")
    .RequireAuthorization();

var systemApi = app.MapGroup("/api/system")
    .RequireAuthorization();

systemApi.MapGet("version", (IWebHostEnvironment environment) =>
{
    var assembly = typeof(Program).Assembly;
    var informationalVersion = assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;
    return Results.Ok(new
    {
        assemblyVersion = assembly.GetName().Version?.ToString(),
        informationalVersion,
        startedAtUtc = appStartedAtUtc,
        environment = environment.EnvironmentName
    });
});

domainTelemetryApi.MapGet("", async Task<IResult> (
    [FromQuery] int? rangeHours,
    IDomainTelemetryService telemetry,
    CancellationToken cancellationToken) =>
{
    var snapshot = await telemetry.GetSnapshotAsync(rangeHours, cancellationToken);
    return Results.Ok(snapshot);
});

domainTelemetryApi.MapGet("logs", async Task<IResult> (
    [FromQuery] int? rangeHours,
    [FromQuery] int? take,
    ApplicationDbContext db,
    CancellationToken cancellationToken) =>
{
    var hours = rangeHours.HasValue ? Math.Clamp(rangeHours.Value, 1, 168) : 24;
    var limit = take.HasValue ? Math.Clamp(take.Value, 10, 1000) : 250;
    var windowStart = DateTime.UtcNow - TimeSpan.FromHours(hours);

    var entries = await db.DomainVerificationLogs
        .AsNoTracking()
        .Where(l => l.CheckedAtUtc >= windowStart)
        .OrderByDescending(l => l.CheckedAtUtc)
        .Take(limit)
        .Select(l => new
        {
            domainId = l.ProjectDomainId,
            status = l.Status.ToString(),
            sslStatus = l.SslStatus.ToString(),
            recordsSatisfied = l.AllRecordsSatisfied,
            failureStreak = l.FailureStreak,
            notificationSent = l.NotificationSent,
            message = l.Message,
            checkedAtUtc = l.CheckedAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(new
    {
        windowStartUtc = windowStart,
        windowEndUtc = DateTime.UtcNow,
        entries
    });
});

var documentTextApi = app.MapGroup("/api/document-text")
    .RequireAuthorization();

documentTextApi.MapPost("save", async Task<IResult> (
    [FromBody] DocumentTextSaveRequest request,
    IDocumentTextService documentService,
    CancellationToken cancellationToken) =>
{
    if (request == null || string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { message = "Text is required." });
    }

    try
    {
        var result = await documentService.SaveAsync(request, cancellationToken);
        return Results.Ok(new
        {
            id = result.Id,
            fileName = result.FileName,
            storedName = result.StoredName,
            savedAtUtc = result.SavedAtUtc
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
}).DisableAntiforgery();

documentTextApi.MapPost("upload", async Task<IResult> (
    IFormFile file,
    IWebHostEnvironment env,
    CancellationToken cancellationToken) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new { message = "File is required." });
    }

    var extension = Path.GetExtension(file.FileName);
    var kind = extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" => "image",
        ".pdf" => "pdf",
        ".docx" => "docx",
        _ => null
    };

    if (kind == null)
    {
        return Results.BadRequest(new { message = "Unsupported file type." });
    }

    var uploadRoot = Path.Combine(env.ContentRootPath, "App_Data", "document-text", "uploads");
    Directory.CreateDirectory(uploadRoot);

    var id = Guid.NewGuid();
    var dataPath = Path.Combine(uploadRoot, $"{id}.bin");
    var metaPath = Path.Combine(uploadRoot, $"{id}.json");

    await using (var stream = File.Create(dataPath))
    {
        await file.CopyToAsync(stream, cancellationToken);
    }

    var meta = new DocumentTextUploadMetadata
    {
        Id = id,
        FileName = file.FileName,
        ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
        Size = file.Length,
        Kind = kind,
        UploadedAtUtc = DateTime.UtcNow
    };

    var metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(metaPath, metaJson, cancellationToken);

    return Results.Ok(new
    {
        id,
        fileName = meta.FileName,
        contentType = meta.ContentType,
        size = meta.Size,
        kind = meta.Kind,
        uploadedAtUtc = meta.UploadedAtUtc
    });
}).DisableAntiforgery();

documentTextApi.MapGet("meta/{id:guid}", async Task<IResult> (
    Guid id,
    IWebHostEnvironment env,
    CancellationToken cancellationToken) =>
{
    var uploadRoot = Path.Combine(env.ContentRootPath, "App_Data", "document-text", "uploads");
    var metaPath = Path.Combine(uploadRoot, $"{id}.json");
    if (!File.Exists(metaPath))
    {
        return Results.NotFound(new { message = "Upload not found." });
    }

    var metaJson = await File.ReadAllTextAsync(metaPath, cancellationToken);
    var meta = JsonSerializer.Deserialize<DocumentTextUploadMetadata>(metaJson);
    if (meta == null)
    {
        return Results.NotFound(new { message = "Upload not found." });
    }

    return Results.Ok(meta);
});

documentTextApi.MapGet("file/{id:guid}", IResult (Guid id, IWebHostEnvironment env) =>
{
    var uploadRoot = Path.Combine(env.ContentRootPath, "App_Data", "document-text", "uploads");
    var metaPath = Path.Combine(uploadRoot, $"{id}.json");
    if (!File.Exists(metaPath))
    {
        return Results.NotFound(new { message = "Upload not found." });
    }

    var metaJson = File.ReadAllText(metaPath);
    var meta = JsonSerializer.Deserialize<DocumentTextUploadMetadata>(metaJson);
    if (meta == null)
    {
        return Results.NotFound(new { message = "Upload not found." });
    }

    var dataPath = Path.Combine(uploadRoot, $"{id}.bin");
    if (!File.Exists(dataPath))
    {
        return Results.NotFound(new { message = "Upload not found." });
    }

    var stream = File.OpenRead(dataPath);
    return Results.File(stream, meta.ContentType ?? "application/octet-stream", meta.FileName, enableRangeProcessing: true);
});

documentTextApi.MapPost("ocr/pdf", async Task<IResult> (
    IFormFile file,
    IDocumentTextService documentService,
    CancellationToken cancellationToken) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new { message = "PDF file is required." });
    }

    var extension = Path.GetExtension(file.FileName);
    if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { message = "Only PDF files are supported." });
    }

    await using var stream = file.OpenReadStream();
    var result = await documentService.OcrPdfAsync(stream, cancellationToken);
    return Results.Ok(new { text = result.Text, pages = result.Pages });
}).DisableAntiforgery();

var contentApi = app.MapGroup("/api/content")
    .RequireAuthorization();

var notificationsApi = app.MapGroup("/api/notifications")
    .RequireAuthorization();

notificationsApi.MapGet("", async (int? take, ApplicationDbContext db, UserManager<ApplicationUser> userManager, HttpContext http) =>
{
    var userId = userManager.GetUserId(http.User);
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    var limit = take.HasValue ? Math.Clamp(take.Value, 1, 50) : 10;
    var items = await db.UserNotifications
        .AsNoTracking()
        .Where(n => n.UserId == userId)
        .OrderByDescending(n => n.CreatedAtUtc)
        .Take(limit)
        .ToListAsync();

    var unreadCount = await db.UserNotifications
        .AsNoTracking()
        .CountAsync(n => n.UserId == userId && !n.IsRead);

    return Results.Ok(new
    {
        unreadCount,
        items = items.Select(n => new
        {
            id = n.Id,
            n.Title,
            n.Message,
            n.Type,
            n.IsRead,
            createdAtUtc = n.CreatedAtUtc
        })
    });
});

notificationsApi.MapPost("read-all", async (ApplicationDbContext db, UserManager<ApplicationUser> userManager, HttpContext http) =>
{
    var userId = userManager.GetUserId(http.User);
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    var items = await db.UserNotifications
        .Where(n => n.UserId == userId && !n.IsRead)
        .ToListAsync();
    foreach (var item in items)
    {
        item.IsRead = true;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { success = true });
});

notificationsApi.MapPost("clear", async (ApplicationDbContext db, UserManager<ApplicationUser> userManager, HttpContext http) =>
{
    var userId = userManager.GetUserId(http.User);
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    var items = await db.UserNotifications
        .Where(n => n.UserId == userId)
        .ToListAsync();
    if (items.Count == 0)
    {
        return Results.Ok(new { success = true, deleted = 0 });
    }

    db.UserNotifications.RemoveRange(items);
    await db.SaveChangesAsync();
    return Results.Ok(new { success = true, deleted = items.Count });
});

contentApi.MapGet("/pages", async Task<IResult> (
    ApplicationDbContext db,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var pages = await db.SitePages
        .AsNoTracking()
        .OrderBy(p => p.Name)
        .Select(p => new
        {
            id = p.Id,
            name = p.Name,
            slug = p.Slug,
            description = p.Description,
            updatedAtUtc = p.UpdatedAtUtc,
            lastPublishedAtUtc = p.Sections
                .Where(s => s.LastPublishedAtUtc != null)
                .Select(s => s.LastPublishedAtUtc)
                .OrderByDescending(v => v)
                .FirstOrDefault(),
            sectionCount = p.Sections.Count,
            textSections = p.Sections.Count(s => s.ContentType == SectionContentType.Text || s.ContentType == SectionContentType.RichText),
            imageSections = p.Sections.Count(s => s.ContentType == SectionContentType.Image)
        })
        .ToListAsync(cancellationToken);

    var totals = new
    {
        pageCount = pages.Count,
        sectionCount = pages.Sum(p => p.sectionCount),
        textSections = pages.Sum(p => p.textSections),
        imageSections = pages.Sum(p => p.imageSections),
        latestUpdateUtc = pages.Select(p => p.updatedAtUtc).OrderByDescending(t => t).FirstOrDefault()
    };

    var etag = GenerateCollectionEtag(pages.Select(p => p.updatedAtUtc.Ticks), pages.Count, "pages");
    if (RequestMatchesEtag(httpContext.Request, etag))
    {
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    httpContext.Response.Headers.ETag = etag;

    return Results.Ok(new
    {
        pages,
        totals,
        etag
    });
});

contentApi.MapGet("/pages/{pageId:guid}", async Task<IResult> (
    Guid pageId,
    ApplicationDbContext db,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var page = await db.SitePages
        .AsNoTracking()
        .Include(p => p.Sections)
        .FirstOrDefaultAsync(p => p.Id == pageId, cancellationToken);

    if (page is null)
    {
        return Results.NotFound();
    }

    var sectionIds = page.Sections.Select(s => s.Id).ToArray();
    var historyLookup = await LoadSectionHistoryAsync(sectionIds, db, cancellationToken);

    var sections = page.Sections
        .OrderBy(s => s.DisplayOrder)
        .Select(s => new
        {
            id = s.Id,
            sectionKey = s.SectionKey,
            title = s.Title,
            contentType = s.ContentType.ToString(),
            contentValue = s.ContentValue,
            mediaPath = s.MediaPath,
            mediaAltText = s.MediaAltText,
            cssSelector = s.CssSelector,
            displayOrder = s.DisplayOrder,
            isLocked = s.IsLocked,
            updatedAtUtc = s.UpdatedAtUtc,
            lastPublishedAtUtc = s.LastPublishedAtUtc,
            previousContentValue = historyLookup.TryGetValue(s.Id, out var log) ? log.PreviousValue : null,
            etag = GenerateSectionEtag(s.Id, s.UpdatedAtUtc)
        })
        .ToList();

    var pageEtag = GeneratePageEtag(page.Id, page.UpdatedAtUtc);
    var sectionsEtag = GenerateCollectionEtag(page.Sections.Select(s => s.UpdatedAtUtc.Ticks), page.Sections.Count, $"sections-{page.Id:N}");

    if (RequestMatchesEtag(httpContext.Request, sectionsEtag))
    {
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    httpContext.Response.Headers.ETag = pageEtag;
    httpContext.Response.Headers["X-Sections-ETag"] = sectionsEtag;

    return Results.Ok(new
    {
        page = new
        {
            page.Id,
            page.Name,
            page.Slug,
            page.Description,
            updatedAtUtc = page.UpdatedAtUtc,
            lastPublishedAtUtc = sections
                .Where(s => s.lastPublishedAtUtc != null)
                .Select(s => s.lastPublishedAtUtc)
                .OrderByDescending(v => v)
                .FirstOrDefault(),
            etag = pageEtag
        },
        sections,
        etag = sectionsEtag
    });
});

contentApi.MapGet("/sections", async Task<IResult> (
    Guid pageId,
    ApplicationDbContext db,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (pageId == Guid.Empty)
    {
        return Results.BadRequest(new { message = "pageId query parameter is required." });
    }

    var page = await db.SitePages
        .AsNoTracking()
        .Include(p => p.Sections)
        .FirstOrDefaultAsync(p => p.Id == pageId, cancellationToken);

    if (page is null)
    {
        return Results.NotFound();
    }

    var historyLookup = await LoadSectionHistoryAsync(page.Sections.Select(s => s.Id).ToArray(), db, cancellationToken);

    var sections = page.Sections
        .OrderBy(s => s.DisplayOrder)
        .Select(s => new
        {
            id = s.Id,
            sectionKey = s.SectionKey,
            title = s.Title,
            contentType = s.ContentType.ToString(),
            contentValue = s.ContentValue,
            mediaPath = s.MediaPath,
            mediaAltText = s.MediaAltText,
            cssSelector = s.CssSelector,
            displayOrder = s.DisplayOrder,
            isLocked = s.IsLocked,
            updatedAtUtc = s.UpdatedAtUtc,
            lastPublishedAtUtc = s.LastPublishedAtUtc,
            previousContentValue = historyLookup.TryGetValue(s.Id, out var log) ? log.PreviousValue : null,
            etag = GenerateSectionEtag(s.Id, s.UpdatedAtUtc)
        })
        .ToList();

    var sectionsEtag = GenerateCollectionEtag(page.Sections.Select(s => s.UpdatedAtUtc.Ticks), page.Sections.Count, $"sections-{page.Id:N}");
    if (RequestMatchesEtag(httpContext.Request, sectionsEtag))
    {
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    httpContext.Response.Headers.ETag = sectionsEtag;

    return Results.Ok(new
    {
        page = new
        {
            page.Id,
            page.Name,
            page.Slug,
            page.Description
        },
        sections,
        etag = sectionsEtag
    });
});

contentApi.MapGet("/history", async Task<IResult> (
    Guid? pageId,
    int? take,
    ApplicationDbContext db,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var limit = take.HasValue && take.Value > 0
        ? Math.Clamp(take.Value, 1, 200)
        : 50;

    var query = db.ContentChangeLogs.AsNoTracking();
    if (pageId.HasValue && pageId.Value != Guid.Empty)
    {
        query = query.Where(log => log.SitePageId == pageId.Value);
    }

    var historyRows = await query
        .OrderByDescending(log => log.PerformedAtUtc)
        .Take(limit)
        .Select(log => new ContentHistoryEntryRecord(
            log.Id,
            log.SitePageId,
            log.PageSectionId,
            log.FieldKey,
            log.PreviousValue,
            log.NewValue,
            log.ChangeSummary,
            log.PerformedByUserId,
            log.PerformedByDisplayName,
            log.PerformedAtUtc))
        .ToListAsync(cancellationToken);

    var scope = pageId.HasValue && pageId.Value != Guid.Empty
        ? $"history-{pageId.Value:N}"
        : "history-all";
    var etag = GenerateCollectionEtag(historyRows.Select(entry => entry.PerformedAtUtc.Ticks), historyRows.Count, scope);
    if (RequestMatchesEtag(httpContext.Request, etag))
    {
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    httpContext.Response.Headers.ETag = etag;

    var history = historyRows.Select(entry => new
    {
        id = entry.Id,
        sitePageId = entry.SitePageId,
        pageSectionId = entry.PageSectionId,
        fieldKey = entry.FieldKey,
        previousValue = entry.PreviousValue,
        newValue = entry.NewValue,
        changeSummary = entry.ChangeSummary,
        performedByUserId = entry.PerformedByUserId,
        performedByDisplayName = entry.PerformedByDisplayName,
        performedAtUtc = entry.PerformedAtUtc,
        diff = BuildHistoryDiff(entry)
    }).ToList();

    return Results.Ok(new
    {
        history,
        etag
    });
});

contentApi.MapPost("/pages/{pageId:guid}/sections", async Task<IResult> (
    Guid pageId,
    [FromForm] SectionUpsertForm form,
    IContentOrchestrator content,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IValidator<SectionUpsertForm> validator,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var validation = await validator.ValidateAsync(form, cancellationToken);
    if (!validation.IsValid)
    {
        return CreateValidationProblem(validation);
    }

    if (!Enum.TryParse<SectionContentType>(form.ContentType, true, out var contentType))
    {
        contentType = SectionContentType.RichText;
    }

    if (form.SectionId.HasValue && form.SectionId.Value != Guid.Empty)
    {
        var existingSection = await content.GetSectionByIdAsync(form.SectionId.Value, cancellationToken);
        if (existingSection is null)
        {
            return Results.NotFound(new { message = "Section not found." });
        }

        var currentEtag = GenerateSectionEtag(existingSection.Id, existingSection.UpdatedAtUtc);
        if (!IfMatchSatisfied(httpContext.Request, currentEtag))
        {
            return Results.Json(
                new { message = "Section has been modified by another editor. Refresh to continue." },
                statusCode: StatusCodes.Status412PreconditionFailed);
        }
    }

    var result = await content.UpsertSectionAsync(
        pageId,
        form.SectionId,
        form.Selector,
        contentType,
        form.ContentValue,
        form.MediaAltText,
        form.Image,
        user.Id,
        user.GetFriendlyName() ?? user.Email ?? "Creator",
        cancellationToken);

    if (!result.Success || result.Section is null)
    {
        return Results.BadRequest(new { message = result.Message ?? "Unable to update section." });
    }

    var section = result.Section;
    var sectionEtag = GenerateSectionEtag(section.Id, section.UpdatedAtUtc);

    var pageRow = await db.SitePages
        .AsNoTracking()
        .Where(p => p.Id == pageId)
        .Select(p => new { p.Id, p.UpdatedAtUtc })
        .FirstOrDefaultAsync(cancellationToken);
    var pageEtag = pageRow is null ? null : GeneratePageEtag(pageRow.Id, pageRow.UpdatedAtUtc);

    httpContext.Response.Headers.ETag = sectionEtag;
    if (!string.IsNullOrEmpty(pageEtag))
    {
        httpContext.Response.Headers["X-Page-ETag"] = pageEtag;
    }

    return Results.Ok(new
    {
        message = result.Message,
        section = new
        {
            section.Id,
            section.SectionKey,
            contentType = section.ContentType.ToString(),
            contentValue = section.ContentValue,
            mediaPath = section.MediaPath,
            mediaAltText = section.MediaAltText,
            cssSelector = section.CssSelector,
            displayOrder = section.DisplayOrder,
            updatedAtUtc = section.UpdatedAtUtc,
            lastPublishedAtUtc = section.LastPublishedAtUtc,
            previousContentValue = result.Log?.PreviousValue,
            etag = sectionEtag
        },
        pageEtag
    });
}).DisableAntiforgery();

contentApi.MapPost("/pages/{pageId:guid}/publish", async Task<IResult> (
    Guid pageId,
    IContentOrchestrator content,
    UserManager<ApplicationUser> userManager,
    HttpContext httpContext,
    ITimelineStore timeline, // timeline injection
    CancellationToken cancellationToken) =>
{
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    try
    {
        var publishResult = await content.PublishPageAsync(
            pageId,
            user.Id,
            user.GetFriendlyName() ?? user.Email ?? "Creator",
            cancellationToken);

        await content.SyncPageFromAssetAsync(pageId, cancellationToken);

        var pageRow = await content.GetPageWithSectionsAsync(pageId, cancellationToken);
        var pageEtag = pageRow is null ? null : GeneratePageEtag(pageRow.Id, pageRow.UpdatedAtUtc);

        if (!string.IsNullOrEmpty(pageEtag))
        {
            httpContext.Response.Headers.ETag = pageEtag;
        }

        return Results.Ok(new
        {
            message = publishResult.Warnings.Count == 0
                ? "Page published to live experience."
                : "Page published with warnings.",
            publishedAtUtc = publishResult.PublishedAtUtc,
            warnings = publishResult.Warnings,
            etag = pageEtag
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
}).DisableAntiforgery();

app.MapGet("/content/canvas/{pageId:guid}", async Task<IResult> (
    Guid pageId,
    IContentOrchestrator content,
    IWebHostEnvironment environment,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    try
    {
        await content.SyncPageFromAssetAsync(pageId, cancellationToken);
        var page = await content.GetPageWithSectionsAsync(pageId, cancellationToken);
        if (page is null)
        {
            return Results.NotFound();
        }

        if (!EditorAssetCatalog.TryResolveAsset(page.Slug, out var fileName))
        {
            return Results.NotFound();
        }

        var physicalPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", fileName));
        if (!File.Exists(physicalPath))
        {
            return Results.NotFound();
        }

        var html = await File.ReadAllTextAsync(physicalPath, cancellationToken);

        var selectorHintMap = EditorAssetCatalog.GetSelectorHints(page.Slug);
        var assetHref = $"/{fileName}";

        var editorConfig = new
        {
            pageId = page.Id,
            pageSlug = page.Slug,
            pageName = page.Name,
            apiBase = "/api/content",
            selectorHints = selectorHintMap,
            pageAsset = assetHref,
            editUrl = $"/Content/Edit/{page.Id}"
        };

        var editorConfigJson = JsonSerializer.Serialize(editorConfig, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var injection = $@"<link rel=""stylesheet"" href=""/editor/bugence-visual-editor.css"" />
<script id=""bugence-editor-config"" type=""application/json"">{editorConfigJson}</script>
<script type=""module"" src=""/editor/bugence-visual-editor.js""></script>
<script src=""/editor/bugence-visual-editor.enhancements.js"" defer></script>
";

        var contentWithInjection = html.Contains("</body>", StringComparison.OrdinalIgnoreCase)
            ? html.Replace("</body>", $"{injection}</body>", StringComparison.OrdinalIgnoreCase)
            : $"{html}{injection}";

        httpContext.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        httpContext.Response.Headers["Pragma"] = "no-cache";
        httpContext.Response.Headers["Expires"] = "0";
        return Results.Content(contentWithInjection, "text/html");
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
    }
}).RequireAuthorization();   //Contant Canvas

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

static async Task<UploadedProject?> FindAccessibleProjectAsync(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ClaimsPrincipal principal,
    int projectId,
    CancellationToken cancellationToken)
{
    var user = await userManager.GetUserAsync(principal);
    if (user == null)
    {
        return null;
    }

    var query = db.UploadedProjects.AsNoTracking().Where(p => p.Id == projectId);
    if (user.CompanyId.HasValue)
    {
        query = query.Where(p => p.CompanyId == user.CompanyId);
    }
    else
    {
        query = query.Where(p => p.UserId == user.Id);
    }

    return await query.FirstOrDefaultAsync(cancellationToken);
}

static async Task<UploadedProject?> FindAccessibleProjectTrackedAsync(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ClaimsPrincipal principal,
    int projectId,
    CancellationToken cancellationToken)
{
    var user = await userManager.GetUserAsync(principal);
    if (user == null)
    {
        return null;
    }

    var query = db.UploadedProjects.Where(p => p.Id == projectId);
    if (user.CompanyId.HasValue)
    {
        query = query.Where(p => p.CompanyId == user.CompanyId);
    }
    else
    {
        query = query.Where(p => p.UserId == user.Id);
    }

    return await query.FirstOrDefaultAsync(cancellationToken);
}

static async Task RebuildProjectFileIndexAsync(ApplicationDbContext db, int projectId, string projectRoot, CancellationToken cancellationToken)
{
    db.UploadedProjectFiles.RemoveRange(db.UploadedProjectFiles.Where(f => f.UploadedProjectId == projectId));

    var files = new List<UploadedProjectFile>();
    foreach (var path in Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(projectRoot, path).Replace("\\", "/");
        var info = new FileInfo(path);
        AddFileIndexEntries(files, projectId, rel, info.Length);
    }

    var distinct = files
        .GroupBy(f => new { f.UploadedProjectId, f.RelativePath, f.IsFolder })
        .Select(g => g.First())
        .ToList();

    db.UploadedProjectFiles.AddRange(distinct);
    await db.SaveChangesAsync(cancellationToken);
}

static void AddFileIndexEntries(List<UploadedProjectFile> files, int projectId, string rel, long sizeBytes)
{
    files.Add(new UploadedProjectFile
    {
        UploadedProjectId = projectId,
        RelativePath = rel,
        SizeBytes = sizeBytes,
        IsFolder = false
    });

    var segments = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var current = string.Empty;
    for (var i = 0; i < segments.Length - 1; i++)
    {
        current = string.IsNullOrEmpty(current) ? segments[i] : $"{current}/{segments[i]}";
        if (files.Any(f => f.IsFolder && f.RelativePath.Equals(current, StringComparison.OrdinalIgnoreCase)))
        {
            continue;
        }

        files.Add(new UploadedProjectFile
        {
            UploadedProjectId = projectId,
            RelativePath = current,
            SizeBytes = 0,
            IsFolder = true
        });
    }
}

static string AttachDynamicVeRuntime(string html, string overlayPath, int? projectId = null, long? revisionId = null)
{
    if (string.IsNullOrWhiteSpace(html))
    {
        return html;
    }

    var cleaned = html;
    cleaned = System.Text.RegularExpressions.Regex.Replace(
        cleaned,
        "<script\\s+id=[\"']bugence-dve-config[\"'][^>]*>.*?</script>",
        string.Empty,
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

    var configJson = JsonSerializer.Serialize(new
    {
        overlayPath,
        debug = false,
        projectId,
        revisionId,
        emittedAtUtc = DateTime.UtcNow
    });
    var configTag = $"<script id=\"bugence-dve-config\" type=\"application/json\">{configJson}</script>";
    const string runtimeTag = "<script id=\"bugence-dve-runtime\" src=\"/js/dynamic-ve-runtime.js\" defer></script>";

    if (!cleaned.Contains("id=\"bugence-dve-runtime\"", StringComparison.OrdinalIgnoreCase))
    {
        if (cleaned.Contains("</body>", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Replace("</body>", $"{runtimeTag}{configTag}</body>", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            cleaned += runtimeTag + configTag;
        }
    }
    else
    {
        if (cleaned.Contains("</body>", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Replace("</body>", $"{configTag}</body>", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            cleaned += configTag;
        }
    }

    return cleaned;
}

static string NormalizeDynamicVePath(string? rawPath)
{
    var value = (rawPath ?? "index.html").Replace("\\", "/").Trim();
    value = value.TrimStart('/');
    if (string.IsNullOrWhiteSpace(value))
    {
        return "index.html";
    }

    var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries)
        .Where(p => p != "." && p != "..")
        .ToArray();
    if (parts.Length == 0)
    {
        return "index.html";
    }
    return string.Join("/", parts);
}

static string BuildDynamicVeHash(string input)
{
    var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty));
    return Convert.ToHexString(bytes);
}

static string BuildDynamicVeElementKey(string pagePath, string selector)
{
    var seed = $"{NormalizeDynamicVePath(pagePath)}::{selector?.Trim() ?? string.Empty}";
    return "el_" + BuildDynamicVeHash(seed)[..16].ToLowerInvariant();
}

static async Task<DynamicVeProjectConfig> EnsureDynamicVeConfigAsync(
    ApplicationDbContext db,
    int projectId,
    CancellationToken cancellationToken)
{
    var config = await db.DynamicVeProjectConfigs.FirstOrDefaultAsync(x => x.UploadedProjectId == projectId, cancellationToken);
    if (config != null)
    {
        return config;
    }

    config = new DynamicVeProjectConfig
    {
        UploadedProjectId = projectId,
        Mode = "overlay",
        RuntimePolicy = "proxy",
        FeatureEnabled = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
    db.DynamicVeProjectConfigs.Add(config);
    await db.SaveChangesAsync(cancellationToken);
    return config;
}

static async Task<DynamicVePageRevision> EnsureDynamicVeDraftRevisionAsync(
    ApplicationDbContext db,
    UploadedProject project,
    string pagePath,
    CancellationToken cancellationToken)
{
    var normalized = NormalizeDynamicVePath(pagePath);
    var config = await EnsureDynamicVeConfigAsync(db, project.Id, cancellationToken);
    DynamicVePageRevision? revision = null;

    if (config.DraftRevisionId.HasValue)
    {
        revision = await db.DynamicVePageRevisions
            .FirstOrDefaultAsync(x => x.Id == config.DraftRevisionId.Value && x.UploadedProjectId == project.Id && x.PagePath == normalized, cancellationToken);
        if (revision != null && (!string.Equals(revision.Environment, "draft", StringComparison.OrdinalIgnoreCase) || !string.Equals(revision.Status, "draft", StringComparison.OrdinalIgnoreCase)))
        {
            revision = null;
        }
    }

    if (revision == null)
    {
        revision = await db.DynamicVePageRevisions
            .Where(x => x.UploadedProjectId == project.Id
                && x.PagePath == normalized
                && x.Environment == "draft"
                && x.Status == "draft")
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    if (revision == null)
    {
        revision = new DynamicVePageRevision
        {
            UploadedProjectId = project.Id,
            PagePath = normalized,
            Environment = "draft",
            Status = "draft",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.DynamicVePageRevisions.Add(revision);
        await db.SaveChangesAsync(cancellationToken);
    }

    if (config.DraftRevisionId != revision.Id)
    {
        config.DraftRevisionId = revision.Id;
        config.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    return revision;
}

static async Task<DynamicVePageRevision> CreateDynamicVeLiveRevisionFromDraftAsync(
    ApplicationDbContext db,
    DynamicVePageRevision draftRevision,
    CancellationToken cancellationToken)
{
    var liveRevision = new DynamicVePageRevision
    {
        UploadedProjectId = draftRevision.UploadedProjectId,
        PagePath = draftRevision.PagePath,
        Environment = "live",
        Status = "published",
        BaseSnapshotId = draftRevision.BaseSnapshotId,
        CreatedByUserId = draftRevision.CreatedByUserId,
        CreatedAtUtc = DateTime.UtcNow
    };
    db.DynamicVePageRevisions.Add(liveRevision);
    await db.SaveChangesAsync(cancellationToken);

    var elementMaps = await db.DynamicVeElementMaps
        .AsNoTracking()
        .Where(x => x.RevisionId == draftRevision.Id)
        .ToListAsync(cancellationToken);
    foreach (var item in elementMaps)
    {
        db.DynamicVeElementMaps.Add(new DynamicVeElementMap
        {
            RevisionId = liveRevision.Id,
            ElementKey = item.ElementKey,
            PrimarySelector = item.PrimarySelector,
            FallbackSelectorsJson = item.FallbackSelectorsJson,
            FingerprintHash = item.FingerprintHash,
            AnchorHash = item.AnchorHash,
            Confidence = item.Confidence,
            LastResolvedSelector = item.LastResolvedSelector,
            LastResolvedAtUtc = item.LastResolvedAtUtc,
            CreatedAtUtc = item.CreatedAtUtc
        });
    }

    var patchRules = await db.DynamicVePatchRules
        .AsNoTracking()
        .Where(x => x.RevisionId == draftRevision.Id)
        .ToListAsync(cancellationToken);
    foreach (var item in patchRules)
    {
        db.DynamicVePatchRules.Add(new DynamicVePatchRule
        {
            RevisionId = liveRevision.Id,
            ElementKey = item.ElementKey,
            RuleType = item.RuleType,
            Breakpoint = item.Breakpoint,
            State = item.State,
            Property = item.Property,
            Value = item.Value,
            Priority = item.Priority,
            CreatedAtUtc = item.CreatedAtUtc
        });
    }

    var textPatches = await db.DynamicVeTextPatches
        .AsNoTracking()
        .Where(x => x.RevisionId == draftRevision.Id)
        .ToListAsync(cancellationToken);
    foreach (var item in textPatches)
    {
        db.DynamicVeTextPatches.Add(new DynamicVeTextPatch
        {
            RevisionId = liveRevision.Id,
            ElementKey = item.ElementKey,
            TextMode = item.TextMode,
            Content = item.Content,
            CreatedAtUtc = item.CreatedAtUtc
        });
    }

    var sections = await db.DynamicVeSectionInstances
        .AsNoTracking()
        .Where(x => x.RevisionId == draftRevision.Id)
        .ToListAsync(cancellationToken);
    foreach (var item in sections)
    {
        db.DynamicVeSectionInstances.Add(new DynamicVeSectionInstance
        {
            RevisionId = liveRevision.Id,
            TemplateId = item.TemplateId,
            InsertMode = item.InsertMode,
            TargetElementKey = item.TargetElementKey,
            MarkupJson = item.MarkupJson,
            CssJson = item.CssJson,
            JsJson = item.JsJson,
            CreatedAtUtc = item.CreatedAtUtc
        });
    }

    var actionBindings = await db.DynamicVeActionBindings
        .AsNoTracking()
        .Where(x => x.RevisionId == draftRevision.Id)
        .ToListAsync(cancellationToken);
    foreach (var item in actionBindings)
    {
        db.DynamicVeActionBindings.Add(new DynamicVeActionBinding
        {
            RevisionId = liveRevision.Id,
            ElementKey = item.ElementKey,
            ActionType = item.ActionType,
            WorkflowId = item.WorkflowId,
            WorkflowDguid = item.WorkflowDguid,
            WorkflowNameSnapshot = item.WorkflowNameSnapshot,
            NavigateUrl = item.NavigateUrl,
            TriggerEvent = item.TriggerEvent,
            ValidationJson = item.ValidationJson,
            BehaviorJson = item.BehaviorJson,
            CreatedAtUtc = item.CreatedAtUtc
        });
    }

    await db.SaveChangesAsync(cancellationToken);
    return liveRevision;
}

static async Task UpsertDynamicVeElementMapAsync(
    ApplicationDbContext db,
    long revisionId,
    string elementKey,
    string selector,
    IEnumerable<string>? fallbackSelectors,
    string? fingerprintHash,
    string? anchorHash,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(selector))
    {
        return;
    }

    var existing = await db.DynamicVeElementMaps
        .FirstOrDefaultAsync(x => x.RevisionId == revisionId && x.ElementKey == elementKey, cancellationToken);
    var fallbackCount = (fallbackSelectors ?? Array.Empty<string>()).Count(x => !string.IsNullOrWhiteSpace(x));
    decimal computedConfidence = 1m;
    if (string.IsNullOrWhiteSpace(selector))
    {
        computedConfidence = fallbackCount > 0 ? 0.72m : 0.5m;
    }
    else if (fallbackCount > 0)
    {
        computedConfidence = 0.9m;
    }
    if (!string.IsNullOrWhiteSpace(fingerprintHash))
    {
        computedConfidence = Math.Max(computedConfidence, 0.75m);
    }

    if (existing != null)
    {
        var normalizedSelector = selector.Trim();
        var normalizedFallbacks = (fallbackSelectors ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var storedFallbacks = new List<string>();
        try
        {
            storedFallbacks = JsonSerializer.Deserialize<List<string>>(existing.FallbackSelectorsJson) ?? new List<string>();
        }
        catch
        {
            storedFallbacks = new List<string>();
        }

        if (!string.IsNullOrWhiteSpace(existing.PrimarySelector) && !existing.PrimarySelector.Equals(normalizedSelector, StringComparison.Ordinal))
        {
            storedFallbacks.Add(existing.PrimarySelector);
        }
        storedFallbacks.AddRange(normalizedFallbacks);
        existing.PrimarySelector = normalizedSelector;
        existing.FallbackSelectorsJson = JsonSerializer.Serialize(
            storedFallbacks
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(12)
                .ToList());
        if (!string.IsNullOrWhiteSpace(fingerprintHash))
        {
            existing.FingerprintHash = fingerprintHash.Trim();
        }
        if (!string.IsNullOrWhiteSpace(anchorHash))
        {
            existing.AnchorHash = anchorHash.Trim();
        }
        existing.LastResolvedSelector = normalizedSelector;
        existing.LastResolvedAtUtc = DateTime.UtcNow;
        existing.Confidence = Math.Min(1m, Math.Max(0.25m, computedConfidence));
        return;
    }

    db.DynamicVeElementMaps.Add(new DynamicVeElementMap
    {
        RevisionId = revisionId,
        ElementKey = elementKey,
        PrimarySelector = selector,
        FallbackSelectorsJson = JsonSerializer.Serialize((fallbackSelectors ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToList()),
        FingerprintHash = string.IsNullOrWhiteSpace(fingerprintHash) ? BuildDynamicVeHash(selector) : fingerprintHash.Trim(),
        AnchorHash = string.IsNullOrWhiteSpace(anchorHash) ? BuildDynamicVeHash(elementKey) : anchorHash.Trim(),
        Confidence = Math.Min(1m, Math.Max(0.25m, computedConfidence)),
        LastResolvedSelector = selector.Trim(),
        LastResolvedAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow
    });
}

static async Task AppendDynamicVeAuditAsync(
    ApplicationDbContext db,
    int projectId,
    long? revisionId,
    UserManager<ApplicationUser> userManager,
    ClaimsPrincipal principal,
    string action,
    object payload,
    CancellationToken cancellationToken)
{
    var user = await userManager.GetUserAsync(principal);
    db.DynamicVeAuditLogs.Add(new DynamicVeAuditLog
    {
        ProjectId = projectId,
        RevisionId = revisionId,
        ActorUserId = user?.Id,
        Action = action,
        PayloadJson = JsonSerializer.Serialize(payload),
        AtUtc = DateTime.UtcNow
    });
    await db.SaveChangesAsync(cancellationToken);
}

static string NormalizeDguid(string? value)
    => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

static string NormalizeTriggerEvent(string? triggerEvent)
{
    var normalized = (triggerEvent ?? "auto").Trim().ToLowerInvariant();
    return normalized switch
    {
        "click" => "click",
        "submit" => "submit",
        _ => "auto"
    };
}

static async Task<Workflow?> ResolveWorkflowByIdOrDguidAsync(
    ApplicationDbContext db,
    Guid? workflowId,
    string? workflowDguid,
    CancellationToken cancellationToken)
{
    if (workflowId.HasValue && workflowId.Value != Guid.Empty)
    {
        return await db.Workflows.FirstOrDefaultAsync(w => w.Id == workflowId.Value, cancellationToken);
    }

    var normalizedDguid = NormalizeDguid(workflowDguid);
    if (string.IsNullOrWhiteSpace(normalizedDguid))
    {
        return null;
    }

    var candidates = await db.Workflows
        .Where(w => w.Dguid != null && w.Dguid != string.Empty)
        .ToListAsync(cancellationToken);
    return candidates.FirstOrDefault(w => NormalizeDguid(w.Dguid) == normalizedDguid);
}

static async Task<string?> ResolveWorkflowRecipientPreviewAsync(
    ApplicationDbContext db,
    Workflow workflow,
    WorkflowTriggerContext context,
    CancellationToken cancellationToken)
{
    if (TryExtractWorkflowEmailNode(workflow.DefinitionJson) is not { } emailNode)
    {
        return context.Email;
    }

    if (!string.IsNullOrWhiteSpace(emailNode.EmailAddress) && emailNode.EmailAddress.Contains('@', StringComparison.Ordinal))
    {
        return emailNode.EmailAddress.Trim();
    }

    if (!string.IsNullOrWhiteSpace(emailNode.MailTo) &&
        emailNode.MailTo.Equals("Current User", StringComparison.OrdinalIgnoreCase))
    {
        var ownerEmail = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == workflow.OwnerUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(ownerEmail))
        {
            return ownerEmail.Trim();
        }
    }

    if (!string.IsNullOrWhiteSpace(emailNode.MailTo) &&
        !emailNode.MailTo.Equals("Current User", StringComparison.OrdinalIgnoreCase) &&
        emailNode.MailTo.Contains('@', StringComparison.Ordinal))
    {
        return emailNode.MailTo.Trim();
    }

    if (!string.IsNullOrWhiteSpace(emailNode.EmailAddressField) &&
        context.Fields.TryGetValue(emailNode.EmailAddressField, out var mappedEmail) &&
        !string.IsNullOrWhiteSpace(mappedEmail))
    {
        return mappedEmail.Trim();
    }

    if (!string.IsNullOrWhiteSpace(context.Email))
    {
        return context.Email.Trim();
    }

    if (context.Fields.TryGetValue("email", out var email) && !string.IsNullOrWhiteSpace(email))
    {
        return email.Trim();
    }

    var fallback = context.Fields
        .FirstOrDefault(kvp => kvp.Key.Contains("email", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kvp.Value));
    return string.IsNullOrWhiteSpace(fallback.Value) ? null : fallback.Value.Trim();
}

static async Task<string> ResolveSmtpProfilePreviewNameAsync(
    ApplicationDbContext db,
    ISensitiveDataProtector protector,
    Workflow workflow,
    CancellationToken cancellationToken)
{
    if (TryExtractWorkflowEmailNode(workflow.DefinitionJson) is { } emailNode
        && !string.IsNullOrWhiteSpace(emailNode.SmtpProfileDguid))
    {
        var selected = await SystemPropertySmtpLoader.FindProfileAsync(
            db,
            protector,
            workflow.OwnerUserId,
            workflow.CompanyId,
            emailNode.SmtpProfileDguid,
            cancellationToken);
        if (selected != null)
        {
            return string.IsNullOrWhiteSpace(selected.Name) ? "selected-profile" : selected.Name;
        }
    }

    var defaultProfile = await SystemPropertySmtpLoader.FindDefaultProfileAsync(
        db,
        protector,
        workflow.OwnerUserId,
        workflow.CompanyId,
        cancellationToken);
    if (defaultProfile != null)
    {
        return string.IsNullOrWhiteSpace(defaultProfile.Name) ? "default-system-profile" : defaultProfile.Name;
    }

    return "app-fallback";
}

static string? MaskEmail(string? email)
{
    if (string.IsNullOrWhiteSpace(email))
    {
        return email;
    }

    var at = email.IndexOf('@');
    if (at <= 1 || at == email.Length - 1)
    {
        return "***";
    }

    var local = email[..at];
    var domain = email[(at + 1)..];
    var maskedLocal = local.Length <= 2
        ? $"{local[0]}*"
        : $"{local[0]}***{local[^1]}";
    return $"{maskedLocal}@{domain}";
}

static (string? MailTo, string? EmailAddress, string? EmailAddressField, string? SmtpProfileDguid)? TryExtractWorkflowEmailNode(string? definitionJson)
{
    if (string.IsNullOrWhiteSpace(definitionJson))
    {
        return null;
    }

    try
    {
        using var doc = JsonDocument.Parse(definitionJson);
        if (!doc.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            if (!node.TryGetProperty("type", out var typeProp) ||
                !string.Equals(typeProp.GetString(), "email", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!node.TryGetProperty("data", out var data))
            {
                continue;
            }

            JsonElement? actionProps = data.TryGetProperty("actionProps", out var actionPropsProp) && actionPropsProp.ValueKind == JsonValueKind.Object
                ? actionPropsProp
                : null;

            string? ActionPropString(string key)
                => actionProps.HasValue && actionProps.Value.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
                    ? prop.GetString()
                    : null;

            return (
                MailTo: ActionPropString("mailTo"),
                EmailAddress: ActionPropString("emailAddress"),
                EmailAddressField: ActionPropString("emailAddressField"),
                SmtpProfileDguid: ActionPropString("smtpProfileDguid"));
        }
    }
    catch
    {
        return null;
    }

    return null;
}

static object MapDomainDto(ProjectDomain domain, UploadedProject? project = null)
{
    var records = domain.DnsRecords ?? Array.Empty<ProjectDomainDnsRecord>();
    var required = records.Count(r => r.IsRequired);
    var satisfied = records.Count(r => r.IsRequired && r.IsSatisfied);
    var targetProject = domain.Project ?? project;
    var publishReady = IsProjectPublishReady(targetProject);
    var isHealthyConnected = domain.Status == DomainStatus.Connected && domain.SslStatus == DomainSslStatus.Active;
    var failure = isHealthyConnected ? (Code: (string?)null, Provider: (string?)null, Message: (string?)null) : ParseFailure(domain.FailureReason);
    var provisioningProvider = string.IsNullOrWhiteSpace(failure.Provider) ? "Webhook" : failure.Provider;
    var hostingStatus = domain.Status switch
    {
        DomainStatus.Connected when publishReady => "Connected",
        DomainStatus.Connected when !publishReady => "Awaiting Project Publish",
        DomainStatus.Verifying when satisfied < required => "Awaiting DNS",
        DomainStatus.Verifying when !publishReady => "Awaiting Project Publish",
        DomainStatus.Verifying when domain.SslStatus == DomainSslStatus.Provisioning => $"Provisioning SSL ({provisioningProvider})",
        DomainStatus.Verifying when domain.SslStatus != DomainSslStatus.Active => "Awaiting SSL",
        _ => domain.Status.ToString()
    };

    return new
    {
        id = domain.Id,
        domain = domain.DomainName,
        type = domain.DomainType.ToString(),
        status = domain.Status.ToString(),
        sslStatus = domain.SslStatus.ToString(),
        verificationToken = domain.VerificationToken,
        createdAtUtc = domain.CreatedAtUtc,
        updatedAtUtc = domain.UpdatedAtUtc,
        lastCheckedAtUtc = domain.LastCheckedAtUtc,
        dnsSatisfied = satisfied,
        dnsRequired = required,
        failureReason = failure.Message ?? domain.FailureReason,
        failureCode = failure.Code,
        failureHint = BuildFailureHint(failure.Code),
        providerUsed = failure.Provider,
        publishReady,
        hostingStatus,
        url = $"https://{domain.DomainName}"
    };
}

static (string? Code, string? Provider, string? Message) ParseFailure(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return (null, null, null);
    }

    var parts = raw.Split('|', 3, StringSplitOptions.TrimEntries);
    if (parts.Length == 1)
    {
        return (null, null, raw);
    }

    if (parts.Length == 2)
    {
        return (parts[0], null, parts[1]);
    }

    return (parts[0], parts[1], parts[2]);
}

static string? BuildFailureHint(string? code)
{
    if (string.IsNullOrWhiteSpace(code))
    {
        return null;
    }

    return code switch
    {
        "DNS_NOT_READY" => "Verify DNS records exactly as shown, then allow propagation.",
        "PUBLISH_NOT_READY" => "Publish the selected project once, then refresh verification.",
        "WEBHOOK_UNREACHABLE" => "Check webhook endpoint health and server network reachability.",
        "WEBHOOK_UNAUTHORIZED" => "Validate webhook API key in System Properties or environment settings.",
        "WEBHOOK_MISCONFIGURED" => "Set CertificateWebhook host, route, and API key correctly.",
        "CERT_ISSUE_FAILED" => "Review ACME/webhook issuer logs and ensure certificate can be issued.",
        "CERT_IMPORT_FAILED" => "Grant cert-store permissions for app identity and verify PFX password.",
        "IIS_BINDING_FAILED" => "Grant IIS management permissions and verify binding privileges.",
        _ => "Check domain verification logs for full diagnostics."
    };
}

static bool IsProjectPublishReady(UploadedProject? project)
{
    if (project == null || string.IsNullOrWhiteSpace(project.PublishStoragePath))
    {
        return false;
    }

    var relative = project.PublishStoragePath
        .Replace('/', Path.DirectorySeparatorChar)
        .TrimStart(Path.DirectorySeparatorChar);

    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"))
    };

    foreach (var webRoot in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (!Directory.Exists(webRoot))
        {
            continue;
        }

        var physicalRoot = Path.Combine(webRoot, relative);
        if (Directory.Exists(physicalRoot) && File.Exists(Path.Combine(physicalRoot, "index.html")))
        {
            return true;
        }
    }

    return false;
}

static bool IsDomainPreflightPath(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return false;
    }

    return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
        path.Contains("/preflight", StringComparison.OrdinalIgnoreCase);
}

static string ExtractDomainIdFromPreflightPath(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return "unknown";
    }

    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (segments.Length >= 3 &&
        segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
        segments[1].Equals("domains", StringComparison.OrdinalIgnoreCase))
    {
        return segments[2];
    }

    return "unknown";
}

static List<DomainPreflightCheck> BuildDomainPreflightChecks(
    ProjectDomain domain,
    IWebHostEnvironment environment,
    CertificateProviderOptions certOptions,
    DomainRoutingOptions routingOptions)
{
    certOptions ??= new CertificateProviderOptions();
    routingOptions ??= new DomainRoutingOptions();

    var checks = new List<DomainPreflightCheck>();
    var requiredDns = (domain.DnsRecords ?? Array.Empty<ProjectDomainDnsRecord>()).Where(r => r.IsRequired).ToList();
    var satisfiedRequired = requiredDns.Count(r => r.IsSatisfied);
    checks.Add(new DomainPreflightCheck
    {
        key = "dns_required",
        required = true,
        ok = requiredDns.Count == 0 || satisfiedRequired == requiredDns.Count,
        detail = $"{satisfiedRequired}/{requiredDns.Count} required DNS records satisfied."
    });

    var publishReady = IsProjectPublishReady(domain.Project);
    checks.Add(new DomainPreflightCheck
    {
        key = "publish_output",
        required = true,
        ok = publishReady,
        detail = publishReady ? "Project publish output detected." : "Project publish output missing (index.html not found)."
    });

    var webhookEndpoint = certOptions.Webhook?.Endpoint;
    var provider = certOptions.Provider ?? string.Empty;
    var webhookConfigured = !string.IsNullOrWhiteSpace(webhookEndpoint) || provider.Equals("Webhook", StringComparison.OrdinalIgnoreCase);
    checks.Add(new DomainPreflightCheck
    {
        key = "webhook_config",
        required = true,
        ok = webhookConfigured,
        detail = webhookConfigured ? "Webhook provider configured." : "Webhook provider endpoint is not configured."
    });

    var localAcme = certOptions.LocalAcme;
    var issueCommand = localAcme?.IssueCommand;
    var scriptPath = ExtractScriptPath(issueCommand);
    var scriptExists = string.IsNullOrWhiteSpace(scriptPath) || File.Exists(scriptPath);
    checks.Add(new DomainPreflightCheck
    {
        key = "local_fallback_script",
        required = localAcme?.Enabled == true,
        ok = scriptExists,
        detail = scriptExists ? "Local fallback command/script is available." : $"Local fallback script missing: {scriptPath}"
    });

    checks.Add(new DomainPreflightCheck
    {
        key = "iis_automation",
        required = true,
        ok = routingOptions.AutoManageIisBindings,
        detail = routingOptions.AutoManageIisBindings ? "IIS automation is enabled." : "IIS automation is disabled in DomainRouting settings."
    });

    return checks;
}

static string ExtractScriptPath(string? command)
{
    if (string.IsNullOrWhiteSpace(command))
    {
        return string.Empty;
    }

    var marker = "-File ";
    var index = command.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (index < 0)
    {
        return string.Empty;
    }

    var tail = command[(index + marker.Length)..].Trim();
    if (tail.StartsWith("\"", StringComparison.Ordinal))
    {
        var end = tail.IndexOf('"', 1);
        return end > 1 ? tail[1..end] : tail.Trim('"');
    }

    var parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return parts.Length == 0 ? string.Empty : parts[0];
}

static IResult CreateValidationProblem(ValidationResult validation)
{
    var errors = validation.Errors
        .GroupBy(error => string.IsNullOrWhiteSpace(error.PropertyName) ? string.Empty : error.PropertyName)
        .ToDictionary(
            group => group.Key,
            group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

    return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);
}

static object? BuildHistoryDiff(ContentHistoryEntryRecord entry)
{
    var changeType = DetermineChangeType(entry.PreviousValue, entry.NewValue);
    if (changeType == "unchanged")
    {
        return null;
    }

    return new
    {
        changeType,
        previousLength = entry.PreviousValue?.Length ?? 0,
        currentLength = entry.NewValue?.Length ?? 0,
        characterDelta = (entry.NewValue?.Length ?? 0) - (entry.PreviousValue?.Length ?? 0),
        containsHtml = ContainsHtml(entry.PreviousValue) || ContainsHtml(entry.NewValue),
        snippet = BuildSnippet(entry.NewValue ?? entry.PreviousValue)
    };
}

static string DetermineChangeType(string? previous, string? current)
{
    if (string.Equals(previous, current, StringComparison.Ordinal))
    {
        return "unchanged";
    }

    if (string.IsNullOrWhiteSpace(previous) && !string.IsNullOrWhiteSpace(current))
    {
        return "added";
    }

    if (!string.IsNullOrWhiteSpace(previous) && string.IsNullOrWhiteSpace(current))
    {
        return "removed";
    }

    return "modified";
}

static string? BuildSnippet(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var snippet = value.Trim()
        .ReplaceLineEndings(" ")
        .Replace("\t", " ");
    return snippet.Length > 160 ? snippet[..160] : snippet;
}

static bool ContainsHtml(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    var indexOfTagStart = value.IndexOf('<');
    var indexOfTagEnd = value.IndexOf('>');
    return indexOfTagStart >= 0 && indexOfTagEnd > indexOfTagStart;
}

static bool RequestMatchesEtag(HttpRequest request, string? etag)
{
    if (string.IsNullOrWhiteSpace(etag))
    {
        return false;
    }

    var header = request.Headers.IfNoneMatch;
    if (header.Count == 0)
    {
        return false;
    }

    foreach (var value in header.SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
    {
        if (string.Equals(value, etag, StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

static bool IfMatchSatisfied(HttpRequest request, string currentEtag)
{
    var values = request.Headers.IfMatch;
    if (values.Count == 0)
    {
        return true;
    }

    foreach (var raw in values.SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
    {
        if (raw == "*")
        {
            return true;
        }

        if (string.Equals(raw, currentEtag, StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

static string GeneratePageEtag(Guid pageId, DateTime updatedAtUtc) =>
    $"W/\"page-{pageId:N}-{updatedAtUtc.Ticks:x}\"";

static string GenerateSectionEtag(Guid sectionId, DateTime updatedAtUtc) =>
    $"W/\"section-{sectionId:N}-{updatedAtUtc.Ticks:x}\"";

static string GenerateCollectionEtag(IEnumerable<long> ticks, int count, string scope)
{
    var max = ticks.DefaultIfEmpty(0).Max();
    return $"W/\"set-{scope}-{max:x}-{count}\"";
}

static async Task<Dictionary<Guid, ContentChangeLog>> LoadSectionHistoryAsync(
    Guid[] sectionIds,
    ApplicationDbContext db,
    CancellationToken cancellationToken)
{
    if (sectionIds.Length == 0)
    {
        return new Dictionary<Guid, ContentChangeLog>();
    }

    return await db.ContentChangeLogs
        .Where(log => log.PageSectionId != null && sectionIds.Contains(log.PageSectionId.Value))
        .OrderByDescending(log => log.PerformedAtUtc)
        .GroupBy(log => log.PageSectionId!.Value)
        .Select(group => new { SectionId = group.Key, Log = group.First() })
        .ToDictionaryAsync(pair => pair.SectionId, pair => pair.Log, cancellationToken);
}

sealed class DocumentTextUploadMetadata
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Kind { get; set; }
    public DateTime UploadedAtUtc { get; set; }
}
