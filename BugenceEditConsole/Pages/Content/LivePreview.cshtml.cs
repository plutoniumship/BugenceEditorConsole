using System.ComponentModel.DataAnnotations;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Content;

public class LivePreviewModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IContentOrchestrator _content;
    private readonly UserManager<ApplicationUser> _userManager;

    public LivePreviewModel(ApplicationDbContext db, IContentOrchestrator content, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _content = content;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public Guid? PageId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? SectionId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int SectionPageIndex { get; set; } = 1;

    [BindProperty]
    public SectionUpdateInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<SitePage> AllPages { get; private set; } = Array.Empty<SitePage>();

    public new SitePage? Page { get; private set; }

    public IReadOnlyList<PageSection> Sections { get; private set; } = Array.Empty<PageSection>();

    public PageSection? ActiveSection { get; private set; }

    public Guid? ActiveSectionId { get; private set; }

    public IReadOnlyList<PageSection> PagedSections { get; private set; } = Array.Empty<PageSection>();

    public int PageSize { get; } = 10;

    public int TotalSectionPages { get; private set; }

    public string BusinessName { get; private set; } = "Bugence Studio";

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAllPagesAsync();

        if (!AllPages.Any())
        {
            return Page();
        }

        var targetId = PageId ?? AllPages.First().Id;
        if (!await LoadPageAsync(targetId))
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadAllPagesAsync();

        if (!AllPages.Any())
        {
            ErrorMessage = "No pages available to update.";
            return RedirectToPage();
        }

        if (PageId is null)
        {
            PageId = AllPages.First().Id;
        }

        if (!ModelState.IsValid)
        {
            SectionId = Input.SectionId;
            await LoadPageAsync(PageId!.Value);

            if (ActiveSection is not null)
            {
                ActiveSection.ContentValue = Input.ContentValue;
                ActiveSection.MediaAltText = Input.MediaAltText;
            }

            return Page();
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

        return RedirectToPage(new { pageId = PageId, sectionId = Input.SectionId });
    }

    private async Task LoadAllPagesAsync() =>
        AllPages = await _db.SitePages.AsNoTracking().OrderBy(p => p.Name).ToListAsync();

    private async Task<bool> LoadPageAsync(Guid pageId)
    {
        Page = await _content.GetPageWithSectionsAsync(pageId);
        if (Page is null)
        {
            return false;
        }

        PageId = pageId;
        var orderedSections = Page.Sections.OrderBy(s => s.DisplayOrder).ToList();
        Sections = orderedSections;
        BusinessName = Page.Name;

        ActiveSectionId = SectionId;
        if (ActiveSectionId is null || orderedSections.All(s => s.Id != ActiveSectionId))
        {
            ActiveSectionId = orderedSections.FirstOrDefault()?.Id;
        }

        ActiveSection = ActiveSectionId is null
            ? null
            : orderedSections.FirstOrDefault(s => s.Id == ActiveSectionId);

        SectionId = ActiveSection?.Id;

        TotalSectionPages = orderedSections.Count == 0
            ? 1
            : (int)Math.Ceiling(orderedSections.Count / (double)PageSize);

        var activeIndex = ActiveSection is null ? -1 : orderedSections.FindIndex(s => s.Id == ActiveSection.Id);
        if (activeIndex >= 0)
        {
            SectionPageIndex = (activeIndex / PageSize) + 1;
        }
        else
        {
            SectionPageIndex = Math.Clamp(SectionPageIndex, 1, TotalSectionPages);
        }

        var skip = (SectionPageIndex - 1) * PageSize;
        PagedSections = orderedSections.Skip(skip).Take(PageSize).ToList();

        return true;
    }

    public class SectionUpdateInput
    {
        [Required]
        public Guid SectionId { get; set; }

        public string? ContentValue { get; set; }

        public string? MediaAltText { get; set; }

        public IFormFile? Image { get; set; }
    }
}

