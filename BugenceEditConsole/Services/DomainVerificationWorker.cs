using BugenceEditConsole.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public class DomainVerificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DomainRoutingOptions _options;
    private readonly ILogger<DomainVerificationWorker> _logger;

    public DomainVerificationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<DomainRoutingOptions> options,
        ILogger<DomainVerificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _options.VerificationInterval;
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromMinutes(5);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VerifyPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Domain verification cycle failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task VerifyPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<IDomainVerificationService>();
        await verifier.VerifyPendingAsync(25, cancellationToken);
    }
}
