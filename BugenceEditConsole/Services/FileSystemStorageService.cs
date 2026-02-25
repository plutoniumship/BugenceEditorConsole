using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace BugenceEditConsole.Services;

public class FileSystemStorageService : IFileStorageService
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg"];
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FileSystemStorageService> _logger;

    public FileSystemStorageService(IWebHostEnvironment environment, ILogger<FileSystemStorageService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<string> SaveAsync(IFormFile file, string folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException("File exceeds the 10 MB limit.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"File type {extension} is not supported.");
        }

        var uploadsRoot = Path.Combine(_environment.WebRootPath ?? "wwwroot", "uploads", folder);
        Directory.CreateDirectory(uploadsRoot);

        var fileName = $"{Path.GetFileNameWithoutExtension(file.FileName).Replace(' ', '_')}_{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(uploadsRoot, fileName);

        await using var stream = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(stream, cancellationToken);

        var relativePath = $"/uploads/{folder}/{fileName}".Replace("\\", "/");
        _logger.LogInformation("Stored file {RelativePath}", relativePath);
        return relativePath;
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Task.CompletedTask;
        }

        try
        {
            var localPath = relativePath.TrimStart('/')
                .Replace("/", Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
            var fullPath = Path.Combine(_environment.WebRootPath ?? "wwwroot", localPath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file {RelativePath}", relativePath);
        }

        return Task.CompletedTask;
    }
}



