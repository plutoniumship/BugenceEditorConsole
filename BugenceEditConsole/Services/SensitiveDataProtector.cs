using Microsoft.AspNetCore.DataProtection;

namespace BugenceEditConsole.Services;

public interface ISensitiveDataProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public class SensitiveDataProtector : ISensitiveDataProtector
{
    private readonly IDataProtector _protector;

    public SensitiveDataProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Bugence.EditConsole.SensitiveData.v1");
    }

    public string Protect(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }
        return _protector.Protect(plainText);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return string.Empty;
        }
        return _protector.Unprotect(protectedText);
    }
}
