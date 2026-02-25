using System.Security.Claims;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages.Settings;

public class EditProfileModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public EditProfileModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public string DisplayName { get; private set; } = "Administrator";
    public string Email { get; private set; } = "admin@bugence.com";
    public string PhoneNumber { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string Timezone { get; private set; } = string.Empty;
    public string Initials { get; private set; } = "AD";

    public async Task OnGetAsync()
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return;
        }

        DisplayName = user.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = user.UserName ?? "Administrator";
        }

        Email = user.Email ?? Email;
        PhoneNumber = user.PhoneNumber ?? string.Empty;
        Country = User.FindFirstValue("bugence:country") ?? string.Empty;
        Timezone = User.FindFirstValue("bugence:timezone") ?? string.Empty;
        Initials = BuildInitials(DisplayName);
    }

    public async Task<IActionResult> OnPostSaveAsync([FromBody] ProfileSaveRequest request)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var fullName = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return BadRequest(new { success = false, message = "Full name is required." });
        }

        var email = request.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(email) && !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var emailResult = await _userManager.SetEmailAsync(user, email);
            if (!emailResult.Succeeded)
            {
                return BadRequest(new { success = false, message = "Unable to update email." });
            }

            if (string.IsNullOrWhiteSpace(user.UserName) || user.UserName.Contains("@", StringComparison.Ordinal))
            {
                await _userManager.SetUserNameAsync(user, email);
            }
        }

        user.DisplayName = fullName;
        var nameParts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        user.FirstName = nameParts.Length > 0 ? nameParts[0] : null;
        user.LastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : null;

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            user.PhoneNumber = request.Phone.Trim();
        }

        await _userManager.UpdateAsync(user);

        await UpsertClaimAsync(user, "bugence:country", request.Country);
        await UpsertClaimAsync(user, "bugence:timezone", request.Timezone);
        await _signInManager.RefreshSignInAsync(user);

        var refreshedName = user.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(refreshedName))
        {
            refreshedName = user.UserName ?? "Administrator";
        }

        return new JsonResult(new
        {
            success = true,
            displayName = refreshedName,
            email = user.Email ?? string.Empty,
            initials = BuildInitials(refreshedName)
        });
    }

    private async Task UpsertClaimAsync(ApplicationUser user, string claimType, string? value)
    {
        var trimmed = value?.Trim();
        var existing = (await _userManager.GetClaimsAsync(user)).FirstOrDefault(c => c.Type == claimType);

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (existing != null)
            {
                await _userManager.RemoveClaimAsync(user, existing);
            }
            return;
        }

        var updated = new Claim(claimType, trimmed);
        if (existing == null)
        {
            await _userManager.AddClaimAsync(user, updated);
            return;
        }

        if (!string.Equals(existing.Value, trimmed, StringComparison.Ordinal))
        {
            await _userManager.ReplaceClaimAsync(user, existing, updated);
        }
    }

    private static string BuildInitials(string name)
    {
        var initials = new string(name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => char.ToUpperInvariant(part[0]))
            .Take(2)
            .ToArray());
        return string.IsNullOrWhiteSpace(initials) ? "AD" : initials;
    }

    public sealed class ProfileSaveRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Country { get; set; }
        public string? Timezone { get; set; }
    }
}
