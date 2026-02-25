using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using BugenceEditConsole.Models;

namespace BugenceEditConsole.Infrastructure;

public class DebugPanelExceptionLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public DebugPanelExceptionLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        DebugPanelLogService debugLogService,
        UserManager<ApplicationUser> userManager)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            string? ownerUserId = null;
            try
            {
                if (context.User?.Identity?.IsAuthenticated == true)
                {
                    var user = await userManager.GetUserAsync(context.User);
                    ownerUserId = user?.Id;
                }
            }
            catch
            {
                // Keep error logging resilient even if user resolution fails.
            }

            await debugLogService.LogErrorAsync(
                source: "UnhandledException",
                shortDescription: ex.Message,
                longDescription: ex.ToString(),
                ownerUserId: ownerUserId,
                path: $"{context.Request.Method} {context.Request.Path}",
                cancellationToken: context.RequestAborted);

            throw;
        }
    }
}
