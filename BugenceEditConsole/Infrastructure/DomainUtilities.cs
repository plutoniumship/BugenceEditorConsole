namespace BugenceEditConsole.Infrastructure;

public static class DomainUtilities
{
    public static string Normalize(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return string.Empty;
        }

        return domain
            .Trim()
            .TrimEnd('.')
            .ToLowerInvariant();
    }

    public static string? GetApex(string? domain)
    {
        var normalized = Normalize(domain);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 2)
        {
            return normalized;
        }

        return string.Join('.', parts[^2..]);
    }
}
