using System.Linq;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages.Content;

public class EditBySlugModel : PageModel
{
    private readonly IContentOrchestrator _content;

    public EditBySlugModel(IContentOrchestrator content)
    {
        _content = content;
    }

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return RedirectToPage("/Content/Library");
        }

        var pages = await _content.GetPagesAsync(HttpContext.RequestAborted);
        var match = pages.FirstOrDefault(page =>
            page is not null &&
            !string.IsNullOrWhiteSpace(page.Slug) &&
            string.Equals(page.Slug, slug, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return RedirectToPage("/Content/Library");
        }

        return RedirectToPage("/Content/Edit", new { pageId = match.Id });
    }
}
