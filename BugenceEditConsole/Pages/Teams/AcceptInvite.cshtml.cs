using System.ComponentModel.DataAnnotations;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Teams;

public class AcceptInviteModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public AcceptInviteModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [BindProperty]
    public AcceptForm Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }
    public bool Success { get; private set; }
    public string? RoleLabel { get; private set; }

    public async Task<IActionResult> OnGetAsync([FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "This invite link is invalid.";
            return Page();
        }

        var invite = await _db.TeamInvites.FirstOrDefaultAsync(i => i.Token == token);
        if (invite == null || invite.ExpiresAtUtc < DateTime.UtcNow)
        {
            ErrorMessage = "This invite link has expired. Please request a new one.";
            return Page();
        }

        if (invite.ConsumedAtUtc != null)
        {
            ErrorMessage = "This invite link has already been used.";
            return Page();
        }

        Input = new AcceptForm
        {
            Token = token,
            Email = invite.Email,
            Role = invite.Role,
            FullName = invite.DisplayNameHint ?? string.Empty
        };
        RoleLabel = invite.Role;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Please complete the required fields.";
            return Page();
        }

        if (!string.Equals(Input.Password, Input.ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "Passwords do not match.";
            return Page();
        }

        var invite = await _db.TeamInvites.FirstOrDefaultAsync(i => i.Token == Input.Token);
        if (invite == null || invite.ExpiresAtUtc < DateTime.UtcNow)
        {
            ErrorMessage = "This invite link has expired. Please request a new one.";
            return Page();
        }

        if (invite.ConsumedAtUtc != null)
        {
            ErrorMessage = "This invite link has already been used.";
            return Page();
        }

        if (!string.Equals(invite.Email, Input.Email, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Invite email does not match.";
            return Page();
        }

        var existingMember = await _db.TeamMembers
            .AnyAsync(m => m.OwnerUserId == invite.OwnerUserId && m.Email == invite.Email);
        if (existingMember)
        {
            invite.ConsumedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            ErrorMessage = "You're already on this team. You can ignore this link.";
            return Page();
        }

        var existingUser = await _userManager.FindByEmailAsync(invite.Email);
        if (existingUser == null)
        {
            var names = Input.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var newUser = new ApplicationUser
            {
                UserName = invite.Email,
                Email = invite.Email,
                EmailConfirmed = true,
                DisplayName = Input.FullName.Trim(),
                FirstName = names.FirstOrDefault(),
                LastName = names.Length > 1 ? string.Join(" ", names.Skip(1)) : null,
                PhoneNumber = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim()
            };

            var createResult = await _userManager.CreateAsync(newUser, Input.Password);
            if (!createResult.Succeeded)
            {
                var msg = string.Join("; ", createResult.Errors.Select(e => e.Description));
                ErrorMessage = msg;
                return Page();
            }

            existingUser = newUser;
        }

        var ownerUser = await _userManager.FindByIdAsync(invite.OwnerUserId);
        if (ownerUser?.CompanyId.HasValue == true && existingUser.CompanyId != ownerUser.CompanyId)
        {
            existingUser.CompanyId = ownerUser.CompanyId;
            existingUser.IsCompanyAdmin = false;
            await _userManager.UpdateAsync(existingUser);
        }

        if (existingUser.CompanyId.HasValue)
        {
            var company = await _db.CompanyProfiles.FirstOrDefaultAsync(c => c.Id == existingUser.CompanyId.Value);
            if (company != null)
            {
                await CompanyDirectoryProvisioningService.EnsureUserCompanyRecordsAsync(
                    _db,
                    existingUser,
                    company,
                    Input.FullName,
                    Input.Phone);
            }
        }

        var member = new TeamMember
        {
            OwnerUserId = invite.OwnerUserId,
            UserId = existingUser.Id,
            Email = invite.Email,
            DisplayName = Input.FullName.Trim(),
            Title = string.IsNullOrWhiteSpace(Input.Title) ? null : Input.Title.Trim(),
            Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim(),
            Role = invite.Role,
            Status = "Active",
            JoinedAtUtc = DateTime.UtcNow
        };

        _db.TeamMembers.Add(member);
        invite.ConsumedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        Success = true;
        RoleLabel = invite.Role;
        return Page();
    }

    public class AcceptForm
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        public string? Title { get; set; }

        public string? Phone { get; set; }

        public string Role { get; set; } = "Editor";

        [Required, MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required, MinLength(8)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
