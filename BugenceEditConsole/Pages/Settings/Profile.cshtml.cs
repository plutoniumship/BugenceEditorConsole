using System.Security.Claims;
using System.Text.Encodings.Web;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Settings;

public class ProfileModel : PageModel
{
    private const string AuthenticatorIssuer = "Bugence";

    private static readonly HashSet<string> SupportedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin",
        "Editor",
        "Viewer"
    };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IWebHostEnvironment _environment;

    public ProfileModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IWebHostEnvironment environment)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _environment = environment;
    }

    public string DisplayName { get; private set; } = "Administrator";
    public string Email { get; private set; } = "admin@bugence.com";
    public string PhoneNumber { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string Timezone { get; private set; } = string.Empty;
    public string Initials { get; private set; } = "AD";
    public string WorkspaceRole { get; private set; } = "Admin";
    public bool CanManageWorkspace { get; private set; }
    public bool CanManageCompany { get; private set; }
    public bool CanChangeName { get; private set; } = true;
    public int NameChangesRemaining { get; private set; } = 1;
    public string? AvatarUrl { get; private set; }

    public string CompanyName { get; private set; } = string.Empty;
    public string CompanyAddressLine1 { get; private set; } = string.Empty;
    public string CompanyAddressLine2 { get; private set; } = string.Empty;
    public string CompanyCity { get; private set; } = string.Empty;
    public string CompanyStateOrProvince { get; private set; } = string.Empty;
    public string CompanyPostalCode { get; private set; } = string.Empty;
    public string CompanyCountry { get; private set; } = string.Empty;
    public string CompanyPhoneNumber { get; private set; } = string.Empty;
    public int? CompanyExpectedUserCount { get; private set; }
    public DateTime? CompanyCreatedAtUtc { get; private set; }
    public string? CompanyLogoUrl { get; private set; }

    public bool TwoFactorEnabled { get; private set; }
    public bool HasAuthenticatorKey { get; private set; }
    public string AuthenticatorKey { get; private set; } = string.Empty;
    public string AuthenticatorUri { get; private set; } = string.Empty;
    public int RecoveryCodesLeft { get; private set; }

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

        await LoadStateAsync(user);
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
                var userNameResult = await _userManager.SetUserNameAsync(user, email);
                if (!userNameResult.Succeeded)
                {
                    return BadRequest(new { success = false, message = "Unable to update username." });
                }
            }
        }

        var currentName = user.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(currentName))
        {
            currentName = user.UserName ?? string.Empty;
        }

        var isNameChanged = !string.Equals(currentName.Trim(), fullName, StringComparison.Ordinal);
        if (isNameChanged && user.NameChangeCount >= 1)
        {
            return BadRequest(new { success = false, message = "Name can only be changed once for this account." });
        }

        user.DisplayName = fullName;
        var nameParts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        user.FirstName = nameParts.Length > 0 ? nameParts[0] : null;
        user.LastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : null;
        if (isNameChanged)
        {
            user.NameChangeCount += 1;
            user.NameLastChangedAtUtc = DateTime.UtcNow;
        }

        user.PhoneNumber = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return BadRequest(new { success = false, message = "Unable to update profile." });
        }

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
            initials = BuildInitials(refreshedName),
            avatarUrl = ToPublicAvatarUrl(user.ProfileImagePath),
            canChangeName = user.NameChangeCount < 1,
            nameChangesRemaining = Math.Max(0, 1 - user.NameChangeCount)
        });
    }

    public async Task<IActionResult> OnPostAvatarAsync(IFormFile? avatar)
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

        if (avatar == null || avatar.Length <= 0)
        {
            return BadRequest(new { success = false, message = "Select an image first." });
        }

        if (avatar.Length > 5 * 1024 * 1024)
        {
            return BadRequest(new { success = false, message = "Image must be under 5 MB." });
        }

        var ext = Path.GetExtension(avatar.FileName)?.ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
        if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
        {
            return BadRequest(new { success = false, message = "Use PNG, JPG, WEBP, or GIF." });
        }

        var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "avatars");
        Directory.CreateDirectory(uploadsRoot);

        DeleteExistingPublicFile(user.ProfileImagePath);

        var fileName = $"{user.Id}-{Guid.NewGuid():N}{ext}";
        var relativePath = $"/uploads/avatars/{fileName}";
        await SaveFormFileAsync(avatar, Path.Combine(uploadsRoot, fileName));

        user.ProfileImagePath = relativePath;
        user.ProfileCompletedAtUtc ??= DateTime.UtcNow;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return BadRequest(new { success = false, message = "Unable to update avatar." });
        }

        await _signInManager.RefreshSignInAsync(user);

        var friendlyName = string.IsNullOrWhiteSpace(user.GetFriendlyName()) ? (user.UserName ?? "Administrator") : user.GetFriendlyName();
        return new JsonResult(new
        {
            success = true,
            avatarUrl = ToPublicAvatarUrl(user.ProfileImagePath),
            displayName = friendlyName,
            initials = BuildInitials(friendlyName)
        });
    }

    public async Task<IActionResult> OnPostCompanySaveAsync([FromBody] CompanySaveRequest request)
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

        var companyAccess = await ResolveCompanyAccessAsync(user);
        if (!companyAccess.CanManage)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "You do not have permission to update company details." });
        }

        var companyName = request.CompanyName?.Trim();
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return BadRequest(new { success = false, message = "Company name is required." });
        }

        var company = companyAccess.Company;
        if (company == null)
        {
            company = new CompanyProfile
            {
                Name = companyName,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = user.Id
            };
            _db.CompanyProfiles.Add(company);
            await _db.SaveChangesAsync();

            user.CompanyId = company.Id;
            user.IsCompanyAdmin = true;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return BadRequest(new { success = false, message = "Unable to attach company to your account." });
            }
        }

        company.Name = companyName;
        company.AddressLine1 = TrimToNull(request.AddressLine1);
        company.AddressLine2 = TrimToNull(request.AddressLine2);
        company.City = TrimToNull(request.City);
        company.StateOrProvince = TrimToNull(request.StateOrProvince);
        company.PostalCode = TrimToNull(request.PostalCode);
        company.Country = TrimToNull(request.Country);
        company.PhoneNumber = TrimToNull(request.PhoneNumber);
        company.ExpectedUserCount = request.ExpectedUserCount > 0 ? request.ExpectedUserCount : null;
        company.CreatedByUserId ??= user.Id;

        await _db.SaveChangesAsync();

        return new JsonResult(new
        {
            success = true,
            company = new
            {
                name = company.Name,
                addressLine1 = company.AddressLine1 ?? string.Empty,
                addressLine2 = company.AddressLine2 ?? string.Empty,
                city = company.City ?? string.Empty,
                stateOrProvince = company.StateOrProvince ?? string.Empty,
                postalCode = company.PostalCode ?? string.Empty,
                country = company.Country ?? string.Empty,
                phoneNumber = company.PhoneNumber ?? string.Empty,
                expectedUserCount = company.ExpectedUserCount,
                logoUrl = ToPublicAvatarUrl(company.LogoPath)
            }
        });
    }

    public async Task<IActionResult> OnPostCompanyLogoAsync(IFormFile? logo)
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

        var companyAccess = await ResolveCompanyAccessAsync(user);
        if (!companyAccess.CanManage || companyAccess.Company == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "You do not have permission to update the company logo." });
        }

        if (logo == null || logo.Length <= 0)
        {
            return BadRequest(new { success = false, message = "Select a logo first." });
        }

        if (logo.Length > 5 * 1024 * 1024)
        {
            return BadRequest(new { success = false, message = "Logo must be under 5 MB." });
        }

        var ext = Path.GetExtension(logo.FileName)?.ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".svg" };
        if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
        {
            return BadRequest(new { success = false, message = "Use PNG, JPG, WEBP, or SVG." });
        }

        var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "company-logos");
        Directory.CreateDirectory(uploadsRoot);

        DeleteExistingPublicFile(companyAccess.Company.LogoPath);

        var fileName = $"{companyAccess.Company.Id}-{Guid.NewGuid():N}{ext}";
        var relativePath = $"/uploads/company-logos/{fileName}";
        await SaveFormFileAsync(logo, Path.Combine(uploadsRoot, fileName));

        companyAccess.Company.LogoPath = relativePath;
        await _db.SaveChangesAsync();

        return new JsonResult(new
        {
            success = true,
            logoUrl = ToPublicAvatarUrl(companyAccess.Company.LogoPath)
        });
    }

    public async Task<IActionResult> OnPostChangePasswordAsync([FromBody] ChangePasswordRequest request)
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

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            string.IsNullOrWhiteSpace(request.NewPassword) ||
            string.IsNullOrWhiteSpace(request.ConfirmPassword))
        {
            return BadRequest(new { success = false, message = "Complete all password fields." });
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return BadRequest(new { success = false, message = "New password confirmation does not match." });
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                success = false,
                message = result.Errors.FirstOrDefault()?.Description ?? "Unable to update password."
            });
        }

        await _signInManager.RefreshSignInAsync(user);
        return new JsonResult(new { success = true, message = "Password updated successfully." });
    }

    public async Task<IActionResult> OnPostEnableTwoFactorAsync([FromBody] EnableTwoFactorRequest request)
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

        var rawCode = request.Code?.Trim();
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return BadRequest(new { success = false, message = "Enter the 6-digit code from your authenticator app." });
        }

        await EnsureAuthenticatorKeyAsync(user);
        var sanitized = NormalizeAuthenticatorCode(rawCode);
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider, sanitized);
        if (!isValid)
        {
            return BadRequest(new { success = false, message = "That verification code is not valid. Check the authenticator app and try again." });
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        await _signInManager.RefreshSignInAsync(user);

        var recoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 8)).ToArray();
        return new JsonResult(new
        {
            success = true,
            message = "Two-factor authentication is now active.",
            recoveryCodes,
            twoFactorEnabled = true
        });
    }

    public async Task<IActionResult> OnPostDisableTwoFactorAsync()
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

        var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!result.Succeeded)
        {
            return BadRequest(new { success = false, message = "Unable to disable two-factor authentication." });
        }

        await _signInManager.RefreshSignInAsync(user);
        return new JsonResult(new
        {
            success = true,
            message = "Two-factor authentication has been disabled.",
            twoFactorEnabled = false
        });
    }

    public async Task<IActionResult> OnPostRefreshTwoFactorAsync()
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

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        await _signInManager.RefreshSignInAsync(user);

        var rawKey = await EnsureAuthenticatorKeyAsync(user);
        return new JsonResult(new
        {
            success = true,
            message = "A fresh authenticator key has been generated.",
            sharedKey = FormatKey(rawKey),
            authenticatorUri = GenerateQrCodeUri(user.Email ?? user.UserName ?? "account", rawKey),
            twoFactorEnabled = false
        });
    }

    public async Task<IActionResult> OnPostRecoveryCodesAsync()
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

        if (!user.TwoFactorEnabled)
        {
            return BadRequest(new { success = false, message = "Enable two-factor authentication first." });
        }

        var recoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 8)).ToArray();
        return new JsonResult(new
        {
            success = true,
            message = "New recovery codes generated.",
            recoveryCodes
        });
    }

    private async Task LoadStateAsync(ApplicationUser user)
    {
        DisplayName = user.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = user.UserName ?? "Administrator";
        }

        Email = user.Email ?? Email;
        PhoneNumber = user.PhoneNumber ?? string.Empty;
        AvatarUrl = ToPublicAvatarUrl(user.ProfileImagePath);
        Country = User.FindFirstValue("bugence:country") ?? string.Empty;
        Timezone = User.FindFirstValue("bugence:timezone") ?? string.Empty;
        NameChangesRemaining = Math.Max(0, 1 - user.NameChangeCount);
        CanChangeName = NameChangesRemaining > 0;
        Initials = BuildInitials(DisplayName);

        var member = await _db.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.Status == "Active");

        if (member == null)
        {
            WorkspaceRole = "Admin";
            CanManageWorkspace = true;
        }
        else
        {
            WorkspaceRole = SupportedRoles.Contains(member.Role) ? member.Role : "Viewer";
            CanManageWorkspace = string.Equals(WorkspaceRole, "Admin", StringComparison.OrdinalIgnoreCase);
        }

        var companyAccess = await ResolveCompanyAccessAsync(user, WorkspaceRole);
        CanManageCompany = companyAccess.CanManage;
        if (companyAccess.Company != null)
        {
            CompanyName = companyAccess.Company.Name;
            CompanyAddressLine1 = companyAccess.Company.AddressLine1 ?? string.Empty;
            CompanyAddressLine2 = companyAccess.Company.AddressLine2 ?? string.Empty;
            CompanyCity = companyAccess.Company.City ?? string.Empty;
            CompanyStateOrProvince = companyAccess.Company.StateOrProvince ?? string.Empty;
            CompanyPostalCode = companyAccess.Company.PostalCode ?? string.Empty;
            CompanyCountry = companyAccess.Company.Country ?? string.Empty;
            CompanyPhoneNumber = companyAccess.Company.PhoneNumber ?? string.Empty;
            CompanyExpectedUserCount = companyAccess.Company.ExpectedUserCount;
            CompanyCreatedAtUtc = companyAccess.Company.CreatedAtUtc;
            CompanyLogoUrl = ToPublicAvatarUrl(companyAccess.Company.LogoPath);
        }

        TwoFactorEnabled = user.TwoFactorEnabled;
        var rawKey = await EnsureAuthenticatorKeyAsync(user);
        HasAuthenticatorKey = !string.IsNullOrWhiteSpace(rawKey);
        AuthenticatorKey = FormatKey(rawKey);
        AuthenticatorUri = GenerateQrCodeUri(user.Email ?? user.UserName ?? "account", rawKey);
        RecoveryCodesLeft = await _userManager.CountRecoveryCodesAsync(user);
    }

    private async Task<CompanyAccessResult> ResolveCompanyAccessAsync(ApplicationUser user, string? workspaceRole = null)
    {
        if (!user.CompanyId.HasValue)
        {
            var fallbackRole = string.IsNullOrWhiteSpace(workspaceRole) ? "Admin" : workspaceRole;
            return new CompanyAccessResult(null, string.Equals(fallbackRole, "Admin", StringComparison.OrdinalIgnoreCase));
        }

        var company = await _db.CompanyProfiles.FirstOrDefaultAsync(c => c.Id == user.CompanyId.Value);
        if (company == null)
        {
            return new CompanyAccessResult(null, false);
        }

        var role = workspaceRole;
        if (string.IsNullOrWhiteSpace(role))
        {
            var member = await _db.TeamMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.UserId == user.Id && m.Status == "Active");
            role = member == null ? "Admin" : (SupportedRoles.Contains(member.Role) ? member.Role : "Viewer");
        }

        var firstUserId = await _db.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == company.Id)
            .OrderBy(u => u.CreatedAtUtc)
            .ThenBy(u => u.Id)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        var canManage = user.IsCompanyAdmin
            || string.Equals(company.CreatedByUserId, user.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(firstUserId, user.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

        return new CompanyAccessResult(company, canManage);
    }

    private async Task<string> EnsureAuthenticatorKeyAsync(ApplicationUser user)
    {
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        await _userManager.ResetAuthenticatorKeyAsync(user);
        key = await _userManager.GetAuthenticatorKeyAsync(user);
        return key ?? string.Empty;
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

    private void DeleteExistingPublicFile(string? publicPath)
    {
        if (string.IsNullOrWhiteSpace(publicPath))
        {
            return;
        }

        var absolute = Path.Combine(_environment.WebRootPath, publicPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(absolute))
        {
            return;
        }

        try
        {
            System.IO.File.Delete(absolute);
        }
        catch
        {
        }
    }

    private static async Task SaveFormFileAsync(IFormFile file, string absolutePath)
    {
        await using var stream = System.IO.File.Create(absolutePath);
        await file.CopyToAsync(stream);
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

    private static string? ToPublicAvatarUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.StartsWith('/') ? path : "/" + path.Replace('\\', '/');
    }

    private static string TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeAuthenticatorCode(string code) =>
        code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static string FormatKey(string unformattedKey)
    {
        if (string.IsNullOrWhiteSpace(unformattedKey))
        {
            return string.Empty;
        }

        var result = new List<string>();
        for (var i = 0; i + 4 <= unformattedKey.Length; i += 4)
        {
            result.Add(unformattedKey.Substring(i, 4).ToLowerInvariant());
        }

        if (unformattedKey.Length % 4 != 0)
        {
            result.Add(unformattedKey[(unformattedKey.Length - (unformattedKey.Length % 4))..].ToLowerInvariant());
        }

        return string.Join(" ", result);
    }

    private static string GenerateQrCodeUri(string email, string unformattedKey)
    {
        return $"otpauth://totp/{UrlEncoder.Default.Encode(AuthenticatorIssuer)}:{UrlEncoder.Default.Encode(email)}?secret={unformattedKey}&issuer={UrlEncoder.Default.Encode(AuthenticatorIssuer)}&digits=6";
    }

    public sealed class ProfileSaveRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Country { get; set; }
        public string? Timezone { get; set; }
    }

    public sealed class CompanySaveRequest
    {
        public string? CompanyName { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? StateOrProvince { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? PhoneNumber { get; set; }
        public int? ExpectedUserCount { get; set; }
    }

    public sealed class ChangePasswordRequest
    {
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
        public string? ConfirmPassword { get; set; }
    }

    public sealed class EnableTwoFactorRequest
    {
        public string? Code { get; set; }
    }

    private sealed record CompanyAccessResult(CompanyProfile? Company, bool CanManage);
}
