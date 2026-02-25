using Microsoft.AspNetCore.Http;

namespace BugenceEditConsole.Models;

public class SectionUpsertForm
{
    public Guid? SectionId { get; set; }

    public string? Selector { get; set; }

    public string? ContentType { get; set; }

    public string? ContentValue { get; set; }

    public string? MediaAltText { get; set; }

    public IFormFile? Image { get; set; }
}
