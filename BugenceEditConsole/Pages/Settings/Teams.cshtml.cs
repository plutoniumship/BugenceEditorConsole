using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Settings;

[Authorize]
public class TeamsModel : PageModel
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin",
        "Editor",
        "Viewer"
    };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;

    public TeamsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailSender emailSender)
    {
        _db = db;
        _userManager = userManager;
        _emailSender = emailSender;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "—";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManageMembers { get; private set; }
    public bool IsOwner { get; private set; }
    public List<TeamMemberRow> Members { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return RedirectToPage("/Auth/Login");
        }

        var user = context.User;
        UserName = user.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(UserName))
        {
            UserName = user.UserName ?? "Administrator";
        }

        UserEmail = string.IsNullOrWhiteSpace(user.Email) ? "—" : user.Email!;
        UserInitials = GetInitials(UserName);
        CanManageMembers = context.CanManageMembers;
        IsOwner = context.IsOwner;

        var now = DateTime.UtcNow;
        var members = await _db.TeamMembers
            .Where(m => m.OwnerUserId == context.OwnerUserId)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();

        var pendingInvites = await _db.TeamInvites
            .Where(i => i.OwnerUserId == context.OwnerUserId && i.ConsumedAtUtc == null && i.ExpiresAtUtc > now)
            .OrderBy(i => i.CreatedAtUtc)
            .ToListAsync();

        var ownerUser = context.OwnerUser ?? user;
        var ownerName = ownerUser.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(ownerName))
        {
            ownerName = ownerUser.UserName ?? "Account Owner";
        }

        var ownerEmail = string.IsNullOrWhiteSpace(ownerUser.Email) ? "—" : ownerUser.Email!;
        Members = new List<TeamMemberRow>
        {
            new()
            {
                Name = ownerName,
                Email = ownerEmail,
                Role = "Owner",
                Status = "Active",
                LastActive = "Now",
                Color = "#8b5cf6",
                Title = "Account Owner",
                IsOwner = true,
                IsCurrentUser = context.IsOwner
            }
        };

        foreach (var member in members)
        {
            Members.Add(new TeamMemberRow
            {
                MemberId = member.Id,
                Name = member.DisplayName,
                Email = member.Email,
                Role = member.Role,
                Status = member.Status,
                LastActive = member.LastActiveAtUtc.HasValue
                    ? member.LastActiveAtUtc.Value.ToLocalTime().ToString("MMM d, yyyy")
                    : "—",
                Color = "#06b6d4",
                Title = member.Title,
                IsCurrentUser = string.Equals(member.UserId, user.Id, StringComparison.Ordinal)
            });
        }

        foreach (var invite in pendingInvites)
        {
            Members.Add(new TeamMemberRow
            {
                InviteId = invite.Id,
                Name = "Pending Invite",
                Email = invite.Email,
                Role = invite.Role,
                Status = "Pending",
                LastActive = "Invite sent",
                Color = "#f59e0b",
                IsPendingInvite = true
            });
        }

        return Page();
    }

    public async Task<IActionResult> OnPostInviteAsync([FromBody] InvitePayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to send invites." }) { StatusCode = 401 };
        }

        if (!context.CanManageMembers)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to invite team members." }) { StatusCode = 403 };
        }

        var user = context.User;
        if (!AllowedRoles.Contains(payload.Role))
        {
            return new JsonResult(new { success = false, message = "Invalid role selected." }) { StatusCode = 400 };
        }

        var requestedEmails = ParseEmails(payload);
        if (requestedEmails.Count == 0)
        {
            return new JsonResult(new { success = false, message = "Please provide at least one email address." }) { StatusCode = 400 };
        }

        var emailValidator = new EmailAddressAttribute();
        var invalidEmails = new List<string>();
        var cleanedEmails = new List<string>();
        foreach (var rawEmail in requestedEmails)
        {
            var trimmed = rawEmail.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (!emailValidator.IsValid(trimmed))
            {
                invalidEmails.Add(trimmed);
                continue;
            }

            cleanedEmails.Add(trimmed);
        }

        var distinctEmails = cleanedEmails
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctEmails.Count == 0)
        {
            var invalidMessage = invalidEmails.Count > 0
                ? $"Invalid email(s): {string.Join(", ", invalidEmails.Take(5))}{(invalidEmails.Count > 5 ? "..." : string.Empty)}"
                : "Please provide a valid email address.";
            return new JsonResult(new { success = false, message = invalidMessage }) { StatusCode = 400 };
        }

        var now = DateTime.UtcNow;
        var normalizedEmails = distinctEmails.Select(e => e.ToLowerInvariant()).ToList();
        var existingMemberEmails = await _db.TeamMembers
            .Where(m => m.OwnerUserId == context.OwnerUserId && normalizedEmails.Contains(m.Email.ToLower()))
            .Select(m => m.Email)
            .ToListAsync();

        var existingInviteEmails = await _db.TeamInvites
            .Where(i => i.OwnerUserId == context.OwnerUserId && i.ConsumedAtUtc == null && i.ExpiresAtUtc > now && normalizedEmails.Contains(i.Email.ToLower()))
            .Select(i => i.Email)
            .ToListAsync();

        var existingMemberSet = new HashSet<string>(existingMemberEmails, StringComparer.OrdinalIgnoreCase);
        var existingInviteSet = new HashSet<string>(existingInviteEmails, StringComparer.OrdinalIgnoreCase);

        var skippedMessages = new List<string>();
        foreach (var invalidEmail in invalidEmails.Take(5))
        {
            skippedMessages.Add($"{invalidEmail} (invalid)");
        }

        var invitesToSend = new List<TeamInvite>();
        var displayNameHint = distinctEmails.Count == 1 && !string.IsNullOrWhiteSpace(payload.FullName)
            ? payload.FullName.Trim()
            : null;

        foreach (var email in distinctEmails)
        {
            if (existingMemberSet.Contains(email))
            {
                skippedMessages.Add($"{email} (already on team)");
                continue;
            }

            if (existingInviteSet.Contains(email))
            {
                skippedMessages.Add($"{email} (active invite exists)");
                continue;
            }

            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var invite = new TeamInvite
            {
                OwnerUserId = context.OwnerUserId,
                Email = email,
                Role = payload.Role,
                DisplayNameHint = displayNameHint,
                Token = token,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7)
            };

            invitesToSend.Add(invite);
            _db.TeamInvites.Add(invite);
        }

        if (invitesToSend.Count == 0)
        {
            var skippedSummary = skippedMessages.Count > 0
                ? $"No invites sent. {string.Join("; ", skippedMessages.Take(5))}{(skippedMessages.Count > 5 ? "..." : string.Empty)}"
                : "No invites sent.";
            return new JsonResult(new { success = false, message = skippedSummary }) { StatusCode = 400 };
        }

        await _db.SaveChangesAsync();

        var inviterName = user.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(inviterName))
        {
            inviterName = user.Email ?? "Bugence Admin";
        }

        var subject = "You're invited to join a Bugence team";
        var deliveryFailures = new List<string>();

        foreach (var invite in invitesToSend)
        {
            var greetingName = string.IsNullOrWhiteSpace(invite.DisplayNameHint) ? "there" : invite.DisplayNameHint.Trim();
            var inviteUrl = Url.Page("/Teams/AcceptInvite", null, new { token = invite.Token }, Request.Scheme);
            var body = $@"
<div style=""font-family: 'Segoe UI', Arial, sans-serif; background:#f7f8fb; padding:30px;"">
  <div style=""max-width:600px; margin:0 auto; background:#ffffff; border-radius:16px; border:1px solid #e7e9ef; overflow:hidden;"">
    <div style=""padding:24px 28px; background:#0f172a; color:#ffffff;"">
      <h1 style=""margin:0; font-size:22px; letter-spacing:0.3px;"">Bugence Team Invite</h1>
      <p style=""margin:6px 0 0; color:#cbd5f5;"">You've been invited to collaborate.</p>
    </div>
    <div style=""padding:28px;"">
      <p style=""margin:0 0 12px; font-size:16px; color:#0f172a;"">Hi {System.Net.WebUtility.HtmlEncode(greetingName)},</p>
      <p style=""margin:0 0 18px; font-size:15px; color:#334155;""><strong>{System.Net.WebUtility.HtmlEncode(inviterName)}</strong> invited you to join their Bugence team as a <strong>{System.Net.WebUtility.HtmlEncode(payload.Role)}</strong>.</p>
      <div style=""text-align:center; margin:28px 0;"">
        <a href=""{inviteUrl}"" style=""background:#06b6d4; color:#00131a; text-decoration:none; font-weight:700; padding:12px 24px; border-radius:10px; display:inline-block;"">Accept Invitation</a>
      </div>
      <p style=""margin:0 0 8px; font-size:13px; color:#64748b;"">This invite link expires in 7 days and can only be used once.</p>
      <p style=""margin:0; font-size:13px; color:#94a3b8;"">If you weren't expecting this, you can safely ignore this email.</p>
    </div>
  </div>
</div>";

            var (sent, error) = await _emailSender.SendAsync(invite.Email, subject, body);
            if (!sent)
            {
                deliveryFailures.Add($"{invite.Email}{(string.IsNullOrWhiteSpace(error) ? string.Empty : $" ({error})")}");
            }
        }

        var summaryMessage = invitesToSend.Count == 1
            ? "Invite sent successfully."
            : $"Invited {invitesToSend.Count} people.";

        var warningParts = new List<string>();
        if (skippedMessages.Count > 0)
        {
            warningParts.Add($"Skipped: {string.Join("; ", skippedMessages.Take(5))}{(skippedMessages.Count > 5 ? "..." : string.Empty)}");
        }
        if (deliveryFailures.Count > 0)
        {
            warningParts.Add($"Delivery issues: {string.Join("; ", deliveryFailures.Take(5))}{(deliveryFailures.Count > 5 ? "..." : string.Empty)}");
        }

        return new JsonResult(new
        {
            success = true,
            message = summaryMessage,
            warning = warningParts.Count > 0 ? string.Join(" | ", warningParts) : null
        });
    }

    public async Task<IActionResult> OnPostUpdateRoleAsync([FromBody] UpdateRolePayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (!context.CanManageMembers)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to update roles." }) { StatusCode = 403 };
        }

        if (payload.MemberId == Guid.Empty || !AllowedRoles.Contains(payload.Role))
        {
            return new JsonResult(new { success = false, message = "Invalid role update request." }) { StatusCode = 400 };
        }

        var member = await _db.TeamMembers
            .FirstOrDefaultAsync(m => m.Id == payload.MemberId && m.OwnerUserId == context.OwnerUserId);
        if (member == null)
        {
            return new JsonResult(new { success = false, message = "Team member not found." }) { StatusCode = 404 };
        }

        if (string.Equals(member.UserId, context.User.Id, StringComparison.Ordinal))
        {
            return new JsonResult(new { success = false, message = "You cannot change your own role." }) { StatusCode = 400 };
        }

        if (string.Equals(member.Role, payload.Role, StringComparison.OrdinalIgnoreCase))
        {
            return new JsonResult(new { success = true, message = "Role already set." });
        }

        member.Role = payload.Role;
        await _db.SaveChangesAsync();
        return new JsonResult(new { success = true, message = "Role updated." });
    }

    public async Task<IActionResult> OnPostRemoveMemberAsync([FromBody] RemoveMemberPayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (!context.CanManageMembers)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to remove members." }) { StatusCode = 403 };
        }

        if (payload.MemberId == Guid.Empty)
        {
            return new JsonResult(new { success = false, message = "Invalid member removal request." }) { StatusCode = 400 };
        }

        var member = await _db.TeamMembers
            .FirstOrDefaultAsync(m => m.Id == payload.MemberId && m.OwnerUserId == context.OwnerUserId);
        if (member == null)
        {
            return new JsonResult(new { success = false, message = "Team member not found." }) { StatusCode = 404 };
        }

        if (string.Equals(member.UserId, context.User.Id, StringComparison.Ordinal))
        {
            return new JsonResult(new { success = false, message = "You cannot remove yourself." }) { StatusCode = 400 };
        }

        _db.TeamMembers.Remove(member);
        await _db.SaveChangesAsync();
        return new JsonResult(new { success = true, message = "Member removed." });
    }

    public async Task<IActionResult> OnPostRemoveInviteAsync([FromBody] RemoveInvitePayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (!context.CanManageMembers)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to delete invites." }) { StatusCode = 403 };
        }

        if (payload.InviteId == Guid.Empty)
        {
            return new JsonResult(new { success = false, message = "Invalid invite removal request." }) { StatusCode = 400 };
        }

        var invite = await _db.TeamInvites
            .FirstOrDefaultAsync(i => i.Id == payload.InviteId && i.OwnerUserId == context.OwnerUserId);
        if (invite == null)
        {
            return new JsonResult(new { success = false, message = "Invite not found." }) { StatusCode = 404 };
        }

        _db.TeamInvites.Remove(invite);
        await _db.SaveChangesAsync();
        return new JsonResult(new { success = true, message = "Invite deleted." });
    }

    private static string GetInitials(string name)
    {
        var initials = new string(name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]))
            .Take(2)
            .ToArray());
        return string.IsNullOrWhiteSpace(initials) ? "AD" : initials;
    }

    public class TeamMemberRow
    {
        public Guid? MemberId { get; set; }
        public Guid? InviteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "Viewer";
        public string Status { get; set; } = "Pending";
        public string LastActive { get; set; } = "—";
        public string Color { get; set; } = "#06b6d4";
        public string? Title { get; set; }
        public bool IsOwner { get; set; }
        public bool IsCurrentUser { get; set; }
        public bool IsPendingInvite { get; set; }
    }

    public class InvitePayload
    {
        public string? Email { get; set; }

        public string? Emails { get; set; }

        public string? FullName { get; set; }

        [Required]
        public string Role { get; set; } = "Editor";
    }

    public class UpdateRolePayload
    {
        [Required]
        public Guid MemberId { get; set; }

        [Required]
        public string Role { get; set; } = "Editor";
    }

    public class RemoveMemberPayload
    {
        [Required]
        public Guid MemberId { get; set; }
    }

    public class RemoveInvitePayload
    {
        [Required]
        public Guid InviteId { get; set; }
    }

    private static List<string> ParseEmails(InvitePayload payload)
    {
        var emails = new List<string>();
        if (!string.IsNullOrWhiteSpace(payload.Email))
        {
            emails.Add(payload.Email);
        }

        if (!string.IsNullOrWhiteSpace(payload.Emails))
        {
            var split = Regex.Split(payload.Emails, @"[,\s;]+", RegexOptions.Compiled);
            emails.AddRange(split);
        }

        return emails;
    }

    private async Task<TeamAccessContext?> GetAccessContextAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return null;
        }

        var currentMember = await _db.TeamMembers
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Status == "Active")
            .OrderByDescending(m => m.JoinedAtUtc)
            .FirstOrDefaultAsync();

        var isOwner = true;
        var ownerUserId = user.Id;
        var canManage = true;
        ApplicationUser? ownerUser = null;

        if (currentMember is not null)
        {
            var ownerCandidate = await _userManager.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == currentMember.OwnerUserId);

            if (ownerCandidate is not null && ownerCandidate.CompanyId == user.CompanyId)
            {
                ownerUser = ownerCandidate;
                ownerUserId = currentMember.OwnerUserId;
                isOwner = false;
                canManage = string.Equals(currentMember.Role, "Admin", StringComparison.OrdinalIgnoreCase);
            }
        }

        ownerUser ??= await _userManager.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == ownerUserId);

        return new TeamAccessContext
        {
            User = user,
            OwnerUser = ownerUser,
            OwnerUserId = ownerUserId,
            IsOwner = isOwner,
            CanManageMembers = canManage
        };
    }

    private sealed class TeamAccessContext
    {
        public required ApplicationUser User { get; init; }
        public ApplicationUser? OwnerUser { get; init; }
        public required string OwnerUserId { get; init; }
        public bool IsOwner { get; init; }
        public bool CanManageMembers { get; init; }
    }
}
