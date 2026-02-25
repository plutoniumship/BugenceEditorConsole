using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages.Auth;

public class DeniedModel : PageModel
{
    public string CorrelationId { get; private set; } = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    public DateTime TimestampUtc { get; private set; } = DateTime.UtcNow;

    public void OnGet()
    {
    }
}

