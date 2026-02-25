namespace BugenceEditConsole.Infrastructure;

public class DomainRoutingOptions
{
    public string PrimaryZone { get; set; } = "bugence.app";
    public string WildcardRecordTarget { get; set; } = string.Empty;
    public string EdgeIpAddress { get; set; } = string.Empty;
    public string PublishRoot { get; set; } = "Published";
    public TimeSpan VerificationInterval { get; set; } = TimeSpan.FromMinutes(5);
    public string IisSiteName { get; set; } = "Default Web Site";
    public bool AutoManageIisBindings { get; set; }
    public bool PerProjectIisSites { get; set; }
    public string IisSiteNamePattern { get; set; } = "Bugence_{ProjectId}";
    public string IisAppPoolPattern { get; set; } = "BugencePool_{ProjectId}";
    public string CertificateStoreName { get; set; } = "My";
    public string CertificateStoreLocation { get; set; } = "LocalMachine";
}
