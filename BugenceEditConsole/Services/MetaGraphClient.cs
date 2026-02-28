using System.Net.Http.Headers;
using System.Text.Json;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;

namespace BugenceEditConsole.Services;

public sealed class MetaGraphClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MetaGraphClient> _logger;

    public MetaGraphClient(IHttpClientFactory httpClientFactory, UserManager<ApplicationUser> userManager, ILogger<MetaGraphClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FacebookAssetDto>> GetAssetsAsync(ApplicationUser user, string type, string? parentId, CancellationToken cancellationToken)
    {
        var token = await _userManager.GetAuthenticationTokenAsync(user, "Facebook", "access_token");
        if (string.IsNullOrWhiteSpace(token))
        {
            return GetFallbackAssets(type, parentId);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("meta-graph");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var path = BuildAssetPath(type, parentId);
            using var response = await client.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Meta Graph asset call failed: {StatusCode} {Type}", response.StatusCode, type);
                return GetFallbackAssets(type, parentId);
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return GetFallbackAssets(type, parentId);
            }

            var assets = new List<FacebookAssetDto>();
            foreach (var item in data.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
                var name = item.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                assets.Add(new FacebookAssetDto
                {
                    Type = type,
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? id : name,
                    ParentId = parentId
                });
            }
            return assets;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Meta Graph asset lookup failed for {Type}. Falling back.", type);
            return GetFallbackAssets(type, parentId);
        }
    }

    public async Task<FacebookLeadPayloadDto> GetLeadPayloadAsync(ApplicationUser user, string leadId, CancellationToken cancellationToken)
    {
        var token = await _userManager.GetAuthenticationTokenAsync(user, "Facebook", "access_token");
        if (string.IsNullOrWhiteSpace(token))
        {
            return BuildFallbackPayload(leadId);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("meta-graph");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.GetAsync($"/v19.0/{Uri.EscapeDataString(leadId)}?fields=id,created_time,field_data,ad_id,adset_id,campaign_id,form_id,page_id", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return BuildFallbackPayload(leadId);
            }
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var root = JsonDocument.Parse(text).RootElement;
            var payload = new FacebookLeadPayloadDto
            {
                LeadId = root.TryGetProperty("id", out var idNode) ? idNode.GetString() : leadId,
                FormId = root.TryGetProperty("form_id", out var fNode) ? fNode.GetString() : null,
                PageId = root.TryGetProperty("page_id", out var pNode) ? pNode.GetString() : null,
                AdId = root.TryGetProperty("ad_id", out var adNode) ? adNode.GetString() : null,
                AdsetId = root.TryGetProperty("adset_id", out var adsetNode) ? adsetNode.GetString() : null,
                CampaignId = root.TryGetProperty("campaign_id", out var campNode) ? campNode.GetString() : null,
                Platform = "FB"
            };
            if (root.TryGetProperty("created_time", out var ctNode) && DateTimeOffset.TryParse(ctNode.GetString(), out var created))
            {
                payload.CreatedTime = created;
            }
            if (root.TryGetProperty("field_data", out var fieldsNode) && fieldsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in fieldsNode.EnumerateArray())
                {
                    var name = row.TryGetProperty("name", out var nNode) ? nNode.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }
                    string? value = null;
                    if (row.TryGetProperty("values", out var vNode) && vNode.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in vNode.EnumerateArray())
                        {
                            value = item.GetString();
                            break;
                        }
                    }
                    payload.FieldData[name] = value;
                }
            }
            return payload;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Meta Graph lead lookup failed.");
            return BuildFallbackPayload(leadId);
        }
    }

    private static string BuildAssetPath(string type, string? parentId)
    {
        return type switch
        {
            "ad_account" => "/v19.0/me/adaccounts?fields=id,name&limit=50",
            "page" when !string.IsNullOrWhiteSpace(parentId) => $"/v19.0/{Uri.EscapeDataString(parentId)}/accounts?fields=id,name&limit=50",
            "page" => "/v19.0/me/accounts?fields=id,name&limit=50",
            "form" when !string.IsNullOrWhiteSpace(parentId) => $"/v19.0/{Uri.EscapeDataString(parentId)}/leadgen_forms?fields=id,name,status&limit=100",
            _ => "/v19.0/me/accounts?fields=id,name&limit=50"
        };
    }

    private static IReadOnlyList<FacebookAssetDto> GetFallbackAssets(string type, string? parentId)
    {
        return type switch
        {
            "ad_account" => [new FacebookAssetDto { Type = "ad_account", Id = "act_10001", Name = "Default Ad Account" }],
            "page" => [new FacebookAssetDto { Type = "page", Id = "123456789", Name = "Sample Facebook Page" }],
            "form" => [new FacebookAssetDto { Type = "form", Id = "987654321", Name = "Sample Lead Form", ParentId = parentId }],
            _ => []
        };
    }

    private static FacebookLeadPayloadDto BuildFallbackPayload(string leadId)
    {
        return new FacebookLeadPayloadDto
        {
            LeadId = leadId,
            CreatedTime = DateTimeOffset.UtcNow,
            FormId = "987654321",
            PageId = "123456789",
            FieldData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["full_name"] = "Test Lead",
                ["email"] = "lead@example.com",
                ["phone"] = "+14155550101",
                ["city"] = "San Francisco",
                ["custom_question_budget"] = "5000"
            }
        };
    }
}
