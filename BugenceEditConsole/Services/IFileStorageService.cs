using Microsoft.AspNetCore.Http;

namespace BugenceEditConsole.Services;

public interface IFileStorageService
{
    Task<string> SaveAsync(IFormFile file, string folder, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}

