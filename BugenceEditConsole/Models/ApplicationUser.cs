using Microsoft.AspNetCore.Identity;

namespace BugenceEditConsole.Models;

public class ApplicationUser : IdentityUser
{
    public Guid? CompanyId { get; set; }
    public CompanyProfile? Company { get; set; }
    public bool IsCompanyAdmin { get; set; }

    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfileImagePath { get; set; }
    public int NameChangeCount { get; set; }
    public DateTime? NameLastChangedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProfileCompletedAtUtc { get; set; }

    public string GetFriendlyName() =>
        string.IsNullOrWhiteSpace(DisplayName)
            ? string.Join(" ", new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)))
            : DisplayName!;
}
