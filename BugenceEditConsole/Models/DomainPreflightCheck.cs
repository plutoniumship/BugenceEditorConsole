namespace BugenceEditConsole.Models;

public sealed class DomainPreflightCheck
{
    public string key { get; set; } = string.Empty;
    public bool required { get; set; }
    public bool ok { get; set; }
    public string detail { get; set; } = string.Empty;

    public bool Required => required;
    public bool Ok => ok;
}
