using System.Text;
using System.Text.Json;

namespace BugenceEditConsole.Services;

public interface IWhatsAppSender
{
    Task<(bool Success, string? ProviderMessageId, string? Error)> SendAsync(string toPhone, string messageText, CancellationToken cancellationToken);
}

public interface ICrmPushService
{
    Task<(bool Success, Dictionary<string, string?> CrmIds, string? Error)> PushAsync(object payload, string mode, CancellationToken cancellationToken);
}

public sealed class WhatsAppWebhookSender : IWhatsAppSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WhatsAppWebhookSender> _logger;

    public WhatsAppWebhookSender(IHttpClientFactory httpClientFactory, ILogger<WhatsAppWebhookSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<(bool Success, string? ProviderMessageId, string? Error)> SendAsync(string toPhone, string messageText, CancellationToken cancellationToken)
    {
        var endpoint = Environment.GetEnvironmentVariable("BUGENCE_WHATSAPP_WEBHOOK_URL");
        var apiKey = Environment.GetEnvironmentVariable("BUGENCE_WHATSAPP_API_KEY");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return (false, null, "WhatsApp delivery is not configured.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("whatsapp-api");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                to = toPhone,
                message = messageText
            }), Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WhatsApp webhook send failed: {StatusCode} {Body}", response.StatusCode, raw);
                return (false, null, $"WhatsApp delivery failed: {(int)response.StatusCode}");
            }

            string? providerMessageId = null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("messageId", out var node))
                {
                    providerMessageId = node.GetString();
                }
            }
            catch
            {
                // ignore response parsing
            }

            return (true, providerMessageId ?? $"wa_{Guid.NewGuid():N}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp webhook send failed for {Phone}", toPhone);
            return (false, null, ex.Message);
        }
    }
}

public sealed class WebhookCrmPushService : ICrmPushService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookCrmPushService> _logger;

    public WebhookCrmPushService(IHttpClientFactory httpClientFactory, ILogger<WebhookCrmPushService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<(bool Success, Dictionary<string, string?> CrmIds, string? Error)> PushAsync(object payload, string mode, CancellationToken cancellationToken)
    {
        var endpoint = Environment.GetEnvironmentVariable("BUGENCE_CRM_WEBHOOK_URL");
        var apiKey = Environment.GetEnvironmentVariable("BUGENCE_CRM_API_KEY");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return (false, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), "CRM push is not configured.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("crm-webhook");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(JsonSerializer.Serialize(new { mode, payload }), Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CRM webhook push failed: {StatusCode} {Body}", response.StatusCode, raw);
                return (false, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), $"CRM push failed: {(int)response.StatusCode}");
            }

            var crmIds = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("crm", out var crmNode) && crmNode.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in crmNode.EnumerateObject())
                    {
                        crmIds[property.Name] = property.Value.GetString();
                    }
                }
            }
            catch
            {
                // ignore response parsing
            }

            return (true, crmIds, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CRM webhook push failed.");
            return (false, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), ex.Message);
        }
    }
}
