using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class CompanyProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? AddressLine1 { get; set; }

    [MaxLength(256)]
    public string? AddressLine2 { get; set; }

    [MaxLength(120)]
    public string? City { get; set; }

    [MaxLength(120)]
    public string? StateOrProvince { get; set; }

    [MaxLength(32)]
    public string? PostalCode { get; set; }

    [MaxLength(120)]
    public string? Country { get; set; }

    [MaxLength(32)]
    public string? PhoneNumber { get; set; }

    public int? ExpectedUserCount { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();

    public ICollection<UploadedProject> UploadedProjects { get; set; } = new List<UploadedProject>();
}
