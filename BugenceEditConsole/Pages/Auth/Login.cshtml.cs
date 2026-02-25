using System.ComponentModel.DataAnnotations;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace BugenceEditConsole.Pages.Auth;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ISessionNonceService _sessionNonceService;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        ISessionNonceService sessionNonceService,
        ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
        _sessionNonceService = sessionNonceService;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public string HighlightedName { get; private set; } = "Pete";

    [TempData]
    public string? ExternalLoginError { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? reason = null, bool signout = false)
    {
        if (signout || string.Equals(reason, "concurrent-login", StringComparison.OrdinalIgnoreCase))
        {
            await _signInManager.SignOutAsync();
        }

        if (_signInManager.IsSignedIn(User))
        {
            return Redirect("/Dashboard/Index");
        }

        ReturnUrl = returnUrl;
        HighlightedName = ExtractNameFromEmail(Input.Email) ?? "Pete";
        return Page();
    }

    public async Task<IActionResult> OnPostExternalLoginAsync(string provider, string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/Dashboard/Index");

        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        if (schemes.All(s => s.Name != provider))
        {
            ExternalLoginError = $"{provider} sign-in is not configured yet.";
            return RedirectToPage(new { returnUrl });
        }

        var redirectUrl = Url.Page("./Login", "ExternalLoginCallback", new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetExternalLoginCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/Dashboard/Index");

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            ExternalLoginError = $"External sign-in error: {remoteError}";
            return RedirectToPage(new { returnUrl });
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            ExternalLoginError = "Unable to load external login information.";
            return RedirectToPage(new { returnUrl });
        }

        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: true);

        if (signInResult.Succeeded)
        {
            _logger.LogInformation("User signed in with {Provider}.", info.LoginProvider);
            var externalUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (externalUser != null && await PermissionSetupOnboardingService.RequiresSetupAsync(_db, externalUser))
            {
                await _sessionNonceService.RotateAsync(externalUser);
                await _signInManager.RefreshSignInAsync(externalUser);
                return LocalRedirect("/Tools/Applications?onboarding=permissions");
            }
            if (externalUser != null)
            {
                await _sessionNonceService.RotateAsync(externalUser);
                await _signInManager.RefreshSignInAsync(externalUser);
            }
            return LocalRedirect(returnUrl);
        }
        if (signInResult.IsLockedOut)
        {
            ExternalLoginError = "Account locked. Please contact support.";
            return RedirectToPage(new { returnUrl });
        }
        if (signInResult.IsNotAllowed)
        {
            ExternalLoginError = "Account not allowed to sign in. Please verify your account or contact support.";
            return RedirectToPage(new { returnUrl });
        }

        var email = info.Principal.FindFirstValue(System.Security.Claims.ClaimTypes.Email)
            ?? info.Principal.FindFirstValue("email");
        if (!string.IsNullOrWhiteSpace(email))
        {
            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                var linkResult = await _userManager.AddLoginAsync(existingUser, info);
                if (linkResult.Succeeded)
                {
                    await _signInManager.SignInAsync(existingUser, isPersistent: false);
                    await _sessionNonceService.RotateAsync(existingUser);
                    await _signInManager.RefreshSignInAsync(existingUser);
                    if (await PermissionSetupOnboardingService.RequiresSetupAsync(_db, existingUser))
                    {
                        return LocalRedirect("/Tools/Applications?onboarding=permissions");
                    }
                    return LocalRedirect(returnUrl);
                }

                ExternalLoginError = "We found your account but could not link the provider. Please try again.";
                return RedirectToPage(new { returnUrl });
            }
        }

        return RedirectToPage("/Auth/Register", new { email, provider = info.LoginProvider, returnUrl });
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/Dashboard/Index");

        if (!ModelState.IsValid)
        {
            HighlightedName = ExtractNameFromEmail(Input.Email) ?? "Creator";
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "We could not find that email. Create an account to continue.");
            HighlightedName = "Pete";
            return Page();
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError(string.Empty, "Account temporarily locked. Please try again later.");
            HighlightedName = user.GetFriendlyName() ?? "Creator";
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await _sessionNonceService.RotateAsync(user);
            await _signInManager.RefreshSignInAsync(user);
            _logger.LogInformation("User {Email} signed in.", user.Email);
            if (await PermissionSetupOnboardingService.RequiresSetupAsync(_db, user))
            {
                return LocalRedirect("/Tools/Applications?onboarding=permissions");
            }
            return LocalRedirect(returnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Account locked due to multiple attempts. Please reset your password.");
            HighlightedName = user.GetFriendlyName() ?? "Creator";
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Invalid credentials. Double check your passphrase or reset access.");
        HighlightedName = user.GetFriendlyName() ?? "Pete";
        return Page();
    }

    private static string? ExtractNameFromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var handle = email.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        var tokens = handle.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", tokens
            .Select(token => char.ToUpperInvariant(token[0]) + token[1..]));
    }
}

