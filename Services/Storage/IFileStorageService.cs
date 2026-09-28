using Microsoft.AspNetCore.Http;

namespace Bizkit_backend.Services.Storage;

public interface IFileStorageService
{
    Task<(bool Succeeded, string? FilePath, string? ErrorMessage)> SaveFileAsync(
        IFormFile file,
        string folderName,
        CancellationToken cancellationToken = default);

    Task DeleteFileAsync(
        string? filePath,
        CancellationToken cancellationToken = default);

    // Kept so existing callers still compile. Prefer DeleteFileAsync.
    void DeleteFile(string? filePath);
}