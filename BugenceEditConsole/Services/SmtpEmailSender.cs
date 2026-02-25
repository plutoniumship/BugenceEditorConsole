using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Bugence";
    public bool EnableSsl { get; set; } = true;
}

public interface IEmailSender
{
    Task<(bool Success, string? Error)> SendAsync(string toEmail, string subject, string htmlBody);
}

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpSettings> settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<(bool Success, string? Error)> SendAsync(string toEmail, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host) || string.IsNullOrWhiteSpace(_settings.FromAddress))
        {
            _logger.LogWarning("SMTP is not configured. Skipping email to {Email}", toEmail);
            return (false, "SMTP is not configured yet.");
        }

        try
        {
            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                EnableSsl = _settings.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Timeout = 10000
            };

            if (!string.IsNullOrWhiteSpace(_settings.Username))
            {
                client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
            }

            var message = new MailMessage
            {
                From = new MailAddress(_settings.FromAddress, _settings.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP send failed for {Email}", toEmail);
            return (false, $"SMTP send failed: {ex.Message}");
        }
    }
}
