using System.Text.RegularExpressions;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bizkit_backend.Services.Storage;

public sealed class FileStorageService(
    Cloudinary cloudinary,
    ILogger<FileStorageService> logger) : IFileStorageService
{
    private static readonly string[] AllowedExtensions =
        [".jpg", ".jpeg", ".png"];

    private static readonly string[] AllowedContentTypes =
        ["image/jpeg", "image/png"];

    private const long MaxFileSizeBytes = 2 * 1024 * 1024;

    private const string CloudinaryHost = "res.cloudinary.com";

    private static readonly Regex VersionSegment =
        new(@"^v\d+$", RegexOptions.Compiled);

    public async Task<(bool Succeeded, string? FilePath, string? ErrorMessage)> SaveFileAsync(
        IFormFile file,
        string folderName,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return (false, null, "No file uploaded.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return (false, null, "File size must not exceed 2MB.");
        }

        var extension = Path.GetExtension(file.FileName)
            .ToLowerInvariant();

        if (Array.IndexOf(AllowedExtensions, extension) < 0)
        {
            return (
                false,
                null,
                "Only .jpg, .jpeg, and .png image files are allowed."
            );
        }

        if (Array.IndexOf(
                AllowedContentTypes,
                file.ContentType?.ToLowerInvariant()) < 0)
        {
            return (
                false,
                null,
                "Invalid file type. Only JPEG and PNG images are allowed."
            );
        }

        await using var stream = file.OpenReadStream();

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = $"bizkit/{folderName}"
        };

        var uploadResult = await cloudinary.UploadAsync(
            uploadParams,
            cancellationToken);

        if (uploadResult.Error != null)
        {
            logger.LogError(
                "Cloudinary upload failed: {Message}",
                uploadResult.Error.Message);

            return (
                false,
                null,
                $"Image upload failed: {uploadResult.Error.Message}"
            );
        }

        return (
            true,
            uploadResult.SecureUrl?.ToString(),
            null
        );
    }

    public async Task DeleteFileAsync(
        string? filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        // Old local paths (e.g. /uploads/products/x.jpg) and non-Cloudinary
        // URLs are ignored.
        if (!Uri.TryCreate(filePath, UriKind.Absolute, out var uri) ||
            !string.Equals(
                uri.Host,
                CloudinaryHost,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var publicId = ExtractPublicId(uri);

        if (string.IsNullOrWhiteSpace(publicId))
        {
            logger.LogWarning(
                "Could not extract Cloudinary public ID from {Url}",
                filePath);
            return;
        }

        try
        {
            var result = await cloudinary.DestroyAsync(
    new DeletionParams(publicId));

            if (result.Error != null || result.Result != "ok")
            {
                logger.LogWarning(
                    "Cloudinary delete for {PublicId} returned '{Result}': {Error}",
                    publicId,
                    result.Result,
                    result.Error?.Message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to delete Cloudinary image {PublicId}",
                publicId);
        }
    }

    public void DeleteFile(string? filePath)
    {
        DeleteFileAsync(filePath).GetAwaiter().GetResult();
    }

    // URL format:
    // https://res.cloudinary.com/{cloud}/image/upload/[transformations/]v123/folder/name.jpg
    // Public ID:   folder/name
    private static string? ExtractPublicId(Uri uri)
    {
        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var uploadIndex = Array.IndexOf(segments, "upload");

        if (uploadIndex < 0 || uploadIndex + 1 >= segments.Length)
        {
            return null;
        }

        var rest = segments.Skip(uploadIndex + 1).ToList();

        // Drop everything up to and including the version segment (v123...),
        // which also removes any transformation segments before it.
        var versionIndex = rest.FindIndex(s => VersionSegment.IsMatch(s));

        if (versionIndex >= 0)
        {
            rest = rest.Skip(versionIndex + 1).ToList();
        }

        var publicIdSegments = rest
            .Select(Uri.UnescapeDataString)
            .ToArray();

        if (publicIdSegments.Length == 0)
        {
            return null;
        }

        var last = publicIdSegments.Length - 1;

        publicIdSegments[last] =
            Path.GetFileNameWithoutExtension(publicIdSegments[last]);

        return string.Join("/", publicIdSegments);
    }
}