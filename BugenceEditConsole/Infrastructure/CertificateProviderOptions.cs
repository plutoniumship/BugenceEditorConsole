namespace BugenceEditConsole.Infrastructure;

public class CertificateProviderOptions
{
    public string Provider { get; set; } = "Stub";
    public WebhookCertificateProviderOptions Webhook { get; set; } = new();
    public LocalAcmeCertificateProviderOptions LocalAcme { get; set; } = new();
}

public class WebhookCertificateProviderOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool IgnoreSslErrors { get; set; }
    public bool ExpectPfx { get; set; } = true;
    public string StorePath { get; set; } = "App_Data/certificates";
}

public class LocalAcmeCertificateProviderOptions
{
    public bool Enabled { get; set; } = true;
    public bool AllowSelfSignedFallback { get; set; } = true;
    public string IssueCommand { get; set; } = "powershell -NoProfile -ExecutionPolicy Bypass -File C:\\tools\\win-acme\\issue-cert.ps1 -Domain {domain} -Apex {apex}";
    public int TimeoutSeconds { get; set; } = 600;
    public string PreferredChallenge { get; set; } = "http-01";
    public string CertificateStoreName { get; set; } = "My";
    public string CertificateStoreLocation { get; set; } = "LocalMachine";
    public string StorePath { get; set; } = "App_Data/certificates";
}
