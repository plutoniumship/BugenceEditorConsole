using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages.Content;

public class EditModel : PageModel
{
    private readonly IContentOrchestrator _content;
    private readonly UserManager<ApplicationUser> _userManager;

    public EditModel(IContentOrchestrator content, UserManager<ApplicationUser> userManager)
    {
        _content = content;
        _userManager = userManager;
    }

    public new SitePage? Page { get; private set; }

    public IReadOnlyList<SitePage> PageOptions { get; private set; } = Array.Empty<SitePage>();

    public IReadOnlyList<PageSection> Sections { get; private set; } = Array.Empty<PageSection>();

    public int SectionCount { get; private set; }

    public int EditableSectionCount { get; private set; }

    public int LockedSectionCount { get; private set; }

    public DateTime? LastPublishedAtUtc { get; private set; }

    [BindProperty]
    public SectionUpdateInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid pageId)
    {
        await _content.SyncPageFromAssetAsync(pageId, HttpContext.RequestAborted);
        var pages = await _content.GetPagesAsync(HttpContext.RequestAborted);
        var page = await _content.GetPageWithSectionsAsync(pageId);
        if (page is null)
        {
            return NotFound();
        }

        HydratePage(page, pages);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(Guid pageId)
    {
        if (!ModelState.IsValid)
        {
            return await ReloadPageAsync(pageId);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await _content.UpdateSectionAsync(
            Input.SectionId,
            Input.ContentValue,
            Input.MediaAltText,
            Input.Image,
            user.Id,
            user.GetFriendlyName() ?? user.Email ?? "Creator");

        if (!result.Success)
        {
            ErrorMessage = result.Message ?? "Unable to update section.";
        }
        else
        {
            StatusMessage = $"{result.Section?.Title ?? result.Section?.SectionKey} saved successfully.";
        }

        return RedirectToPage(new { pageId });
    }

    public async Task<IActionResult> OnPostPublishAsync(Guid pageId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        try
        {
            var publishResult = await _content.PublishPageAsync(pageId, user.Id, user.GetFriendlyName() ?? user.Email ?? "Creator");
            await _content.SyncPageFromAssetAsync(pageId, HttpContext.RequestAborted);

            var publishedLocal = publishResult.PublishedAtUtc.ToLocalTime().ToString("MMM dd, yyyy HH:mm");
            StatusMessage = $"Page published to live site at {publishedLocal}.";
            if (publishResult.Warnings.Count > 0)
            {
                StatusMessage += $" (Skipped: {string.Join(", ", publishResult.Warnings)}).";
            }
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { pageId });
    }

    private async Task<IActionResult> ReloadPageAsync(Guid pageId)
    {
        await _content.SyncPageFromAssetAsync(pageId, HttpContext.RequestAborted);
        var pages = await _content.GetPagesAsync(HttpContext.RequestAborted);
        var page = await _content.GetPageWithSectionsAsync(pageId);
        if (page is null)
        {
            return NotFound();
        }

        HydratePage(page, pages);
        return Page();
    }

    private void HydratePage(SitePage page, IReadOnlyList<SitePage>? pages = null)
    {
        Page = page;
        Sections = page.Sections.OrderBy(s => s.DisplayOrder).ToList();

        var options = new List<SitePage>();

        void AddOption(SitePage candidate)
        {
            if (!options.Any(p => p.Id == candidate.Id))
            {
                options.Add(candidate);
            }
        }

        AddOption(page);

        if (pages is not null)
        {
            foreach (var candidate in pages.OrderBy(p => p.Name))
            {
                AddOption(candidate);
            }
        }

        PageOptions = options;

        SectionCount = Sections.Count;
        LockedSectionCount = Sections.Count(s => s.IsLocked);
        EditableSectionCount = SectionCount - LockedSectionCount;
        LastPublishedAtUtc = Sections
            .Where(s => s.LastPublishedAtUtc.HasValue)
            .OrderByDescending(s => s.LastPublishedAtUtc)
            .Select(s => s.LastPublishedAtUtc)
            .FirstOrDefault();
    }

    public class SectionUpdateInput
    {
        [Required]
        public Guid SectionId { get; set; }

        [Display(Name = "Content")]
        public string? ContentValue { get; set; }

        [Display(Name = "Image Alt Text")]
        public string? MediaAltText { get; set; }

        public IFormFile? Image { get; set; }
    }
}




