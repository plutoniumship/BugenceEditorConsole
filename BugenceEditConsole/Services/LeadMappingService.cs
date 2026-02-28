using System.Globalization;
using System.Text.Json;
using BugenceEditConsole.Models;

namespace BugenceEditConsole.Services;

public sealed class LeadMappingService
{
    public (Dictionary<string, string?> MappedFields, Dictionary<string, bool> ValidationFlags) Apply(
        FacebookLeadPayloadDto payload,
        FacebookLeadTriggerConfigDto config)
    {
        var mapped = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in payload.FieldData)
        {
            mapped[kvp.Key] = kvp.Value;
        }

        foreach (var rule in config.MappingRules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.SourceField) || string.IsNullOrWhiteSpace(rule.TargetField))
            {
                continue;
            }

            payload.FieldData.TryGetValue(rule.SourceField, out var value);
            mapped[rule.TargetField] = ApplyTransform(value, rule.Transform, config);
        }

        if (config.SplitFullName)
        {
            var fullName = First(mapped, "full_name", "fullName", "name");
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                mapped["first_name"] = parts.Length > 0 ? parts[0] : string.Empty;
                mapped["last_name"] = parts.Length > 1 ? parts[1] : string.Empty;
            }
        }

        if (config.NormalizePhone)
        {
            var phone = First(mapped, "phone", "phone_number", "mobile");
            if (!string.IsNullOrWhiteSpace(phone))
            {
                mapped["phone"] = NormalizePhone(phone);
                if (config.DetectCountryFromPhone)
                {
                    mapped["country"] = DetectCountry(mapped["phone"]);
                }
            }
        }

        if (config.ValidateEmail)
        {
            var email = First(mapped, "email");
            mapped["email"] = email;
        }

        var hasEmail = !string.IsNullOrWhiteSpace(mapped.TryGetValue("email", out var resolvedEmail) ? resolvedEmail : null);
        var hasPhone = !string.IsNullOrWhiteSpace(mapped.TryGetValue("phone", out var resolvedPhone) ? resolvedPhone : null);
        var hasConsent = payload.ConsentFlags.Values.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase));

        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["has_contact"] = hasEmail || hasPhone,
            ["has_email"] = hasEmail,
            ["has_phone"] = hasPhone,
            ["has_consent"] = hasConsent,
            ["valid_email"] = !hasEmail || IsLikelyEmail(resolvedEmail)
        };

        return (mapped, flags);
    }

    public string BuildMappingJson(IReadOnlyList<LeadMappingRuleDto> rules)
        => JsonSerializer.Serialize(rules ?? []);

    private static string? ApplyTransform(string? value, string? transform, FacebookLeadTriggerConfigDto config)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var normalized = (transform ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "uppercase" => value.ToUpperInvariant(),
            "lowercase" => value.ToLowerInvariant(),
            "trim" => value.Trim(),
            "phone_normalize" => config.NormalizePhone ? NormalizePhone(value) : value,
            _ => value
        };
    }

    private static string? First(IReadOnlyDictionary<string, string?> source, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (source.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizePhone(string value)
    {
        var chars = value.Where(c => char.IsDigit(c) || c == '+').ToArray();
        var cleaned = new string(chars);
        if (cleaned.StartsWith("00", StringComparison.Ordinal))
        {
            cleaned = "+" + cleaned[2..];
        }
        if (!cleaned.StartsWith("+", StringComparison.Ordinal) && cleaned.Length == 10)
        {
            cleaned = "+1" + cleaned;
        }
        return cleaned;
    }

    private static string? DetectCountry(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }
        if (phone.StartsWith("+1", StringComparison.Ordinal)) return "US";
        if (phone.StartsWith("+44", StringComparison.Ordinal)) return "GB";
        if (phone.StartsWith("+92", StringComparison.Ordinal)) return "PK";
        return null;
    }

    private static bool IsLikelyEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var at = value.IndexOf('@');
        var dot = value.LastIndexOf('.');
        return at > 0 && dot > at + 1 && dot < value.Length - 1;
    }
}
