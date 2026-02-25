using System.Security.Claims;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;

namespace BugenceEditConsole.Services;

public interface ISessionNonceService
{
    string ClaimType { get; }
    Task<string> RotateAsync(ApplicationUser user);
    Task<string?> GetCurrentNonceAsync(ApplicationUser user);
}

public class SessionNonceService : ISessionNonceService
{
    public const string SessionNonceClaimType = "bugence:session_nonce";
    private readonly UserManager<ApplicationUser> _userManager;

    public SessionNonceService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public string ClaimType => SessionNonceClaimType;

    public async Task<string> RotateAsync(ApplicationUser user)
    {
        var nextNonce = Guid.NewGuid().ToString("N");
        var claims = await _userManager.GetClaimsAsync(user);

        foreach (var claim in claims.Where(c => c.Type == SessionNonceClaimType).ToList())
        {
            await _userManager.RemoveClaimAsync(user, claim);
        }

        await _userManager.AddClaimAsync(user, new Claim(SessionNonceClaimType, nextNonce));
        return nextNonce;
    }

    public async Task<string?> GetCurrentNonceAsync(ApplicationUser user)
    {
        var claims = await _userManager.GetClaimsAsync(user);
        return claims.FirstOrDefault(c => c.Type == SessionNonceClaimType)?.Value;
    }
}
