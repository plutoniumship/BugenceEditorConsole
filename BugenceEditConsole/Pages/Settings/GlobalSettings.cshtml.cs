using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages.Settings;

public class GlobalSettingsModel : PageModel
{
    public IActionResult OnGet()
    {
        return Page();
    }
}
