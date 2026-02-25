using System.ComponentModel.DataAnnotations;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Auth;

public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;

    public ForgotPasswordModel(UserManager<ApplicationUser> userManager, ApplicationDbContext db, IEmailSender emailSender)
    {
        _userManager = userManager;
        _db = db;
        _emailSender = emailSender;
    }

    public IActionResult OnGet()
    {
        return Page();
    }

    public async Task<IActionResult> OnPostSendOtpAsync([FromForm] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new JsonResult(new { success = false, message = "Email is required." }) { StatusCode = 400 };
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "No account found for that email." }) { StatusCode = 400 };
        }

        var code = new Random().Next(100000, 999999).ToString();
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var ticket = new PasswordResetTicket
        {
            Email = email.Trim(),
            Token = token,
            VerificationCode = code,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            IsConsumed = false
        };

        _db.PasswordResetTickets.Add(ticket);
        await _db.SaveChangesAsync();

        var subject = "Reset your Bugence password";
        var body = $"<p>Your Bugence verification code is:</p><h2>{code}</h2><p>This code expires in 15 minutes.</p>";
        var (sent, error) = await _emailSender.SendAsync(email, subject, body);

        return new JsonResult(new { success = true, warning = sent ? null : error });
    }

    public async Task<IActionResult> OnPostResetAsync([FromBody] ResetPayload payload)
    {
        if (!ModelState.IsValid)
        {
            return new JsonResult(new { success = false, message = "Please fill all required fields." }) { StatusCode = 400 };
        }
        if (!string.Equals(payload.NewPassword, payload.ConfirmPassword, StringComparison.Ordinal))
        {
            return new JsonResult(new { success = false, message = "Passwords do not match." }) { StatusCode = 400 };
        }

        var ticket = await _db.PasswordResetTickets
            .Where(t => t.Email == payload.Email && !t.IsConsumed)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (ticket == null || ticket.ExpiresAtUtc < DateTime.UtcNow)
        {
            return new JsonResult(new { success = false, message = "Reset code expired. Request a new one." }) { StatusCode = 400 };
        }

        if (!string.Equals(ticket.VerificationCode, payload.Code, StringComparison.OrdinalIgnoreCase))
        {
            return new JsonResult(new { success = false, message = "Invalid verification code." }) { StatusCode = 400 };
        }

        var user = await _userManager.FindByEmailAsync(payload.Email);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Account not found." }) { StatusCode = 400 };
        }

        var result = await _userManager.ResetPasswordAsync(user, ticket.Token, payload.NewPassword);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            return new JsonResult(new { success = false, message = msg }) { StatusCode = 400 };
        }

        ticket.IsConsumed = true;
        await _db.SaveChangesAsync();

        return new JsonResult(new { success = true, redirectUrl = "/Auth/Login?reset=success" });
    }

    public class ResetPayload
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;

        [Required]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
