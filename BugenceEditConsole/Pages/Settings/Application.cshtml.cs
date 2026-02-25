
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages.Settings;

public class ApplicationModel : PageModel
{
    public string AppVersion { get; private set; } = string.Empty;

    public string EnvironmentName { get; private set; } = string.Empty;

    public string ContentRoot { get; private set; } = string.Empty;

    public string DatabasePath { get; private set; } = string.Empty;

    public void OnGet()
    {
        var assembly = typeof(ApplicationModel).Assembly.GetName();
        AppVersion = assembly.Version?.ToString() ?? "1.0.0";
        EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        ContentRoot = Directory.GetCurrentDirectory();
        DatabasePath = Path.Combine(ContentRoot, "app.db");
    }
}

