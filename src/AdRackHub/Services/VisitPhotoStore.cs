using System.Globalization;
using AdRackHub.Models;

namespace AdRackHub.Services;

public sealed class StoredVisitImage
{
    public required byte[] Bytes { get; init; }
    public required string OriginalFileName { get; init; }
    public required string ContentType { get; init; }
    public required string Extension { get; init; }
}

public static class VisitImageRules
{
    public const long MaxBytes = 8 * 1024 * 1024;

    public static StoredVisitImage FromUpload(IFormFile? file, string label)
    {
        if (file == null || file.Length == 0)
            throw new InvalidOperationException($"Choose a {label.ToLowerInvariant()}.");
        if (file.Length > MaxBytes)
            throw new InvalidOperationException($"{label} must be 8 MB or smaller.");

        using var stream = new MemoryStream();
        file.CopyTo(stream);
        return Finish(stream.ToArray(), file.FileName, label);
    }

    public static StoredVisitImage FromBase64(string? raw, string? fileName, string label)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException($"{label} is required.");

        var text = raw.Trim();
        var comma = text.IndexOf(',');
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
            text = text[(comma + 1)..];
        text = text.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{label} is not valid base64. Pass the email attachment bytes, or a data URL.");
        }

        if (bytes.Length == 0)
            throw new InvalidOperationException($"{label} is required.");
        if (bytes.Length > MaxBytes)
            throw new InvalidOperationException($"{label} must be 8 MB or smaller.");

        return Finish(bytes, fileName, label);
    }

    public static DateTime ParseVisitedAt(string? visitedAt)
    {
        if (string.IsNullOrWhiteSpace(visitedAt))
            return DateTime.UtcNow;

        if (!DateTime.TryParse(visitedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            && !DateTime.TryParse(visitedAt, out parsed))
            throw new InvalidOperationException("visitedAt must be a date and time.");

        return parsed.Kind switch
        {
            DateTimeKind.Utc => parsed,
            DateTimeKind.Local => parsed.ToUniversalTime(),
            _ => DateTime.SpecifyKind(parsed, DateTimeKind.Local).ToUniversalTime()
        };
    }

    private static StoredVisitImage Finish(byte[] bytes, string? fileName, string label)
    {
        var (contentType, extension) = Detect(bytes, label);
        var original = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrWhiteSpace(original))
            original = "photo" + extension;
        if (original.Length > 255)
            original = original[^255..];

        return new StoredVisitImage
        {
            Bytes = bytes,
            OriginalFileName = original,
            ContentType = contentType,
            Extension = extension
        };
    }

    private static (string ContentType, string Extension) Detect(byte[] bytes, string label)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("image/jpeg", ".jpg");
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ("image/png", ".png");
        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F')
            return ("image/gif", ".gif");
        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return ("image/webp", ".webp");

        throw new InvalidOperationException($"{label} must be a JPG, PNG, GIF, or WEBP image.");
    }
}

public class VisitPhotoStore
{
    private readonly string _uploadRoot;

    public VisitPhotoStore(IWebHostEnvironment environment)
        : this(Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", "visits"))
    {
    }

    public VisitPhotoStore(string uploadRoot)
    {
        _uploadRoot = uploadRoot;
    }

    public string DirectoryFor(int visitId) => Path.Combine(_uploadRoot, visitId.ToString());

    public string PathFor(StopVisitPhoto photo)
    {
        var stored = Path.GetFileName(photo.StoredFileName);
        return Path.Combine(DirectoryFor(photo.StopVisitId), stored);
    }

    public void DeleteVisitFolder(int visitId)
    {
        var directory = DirectoryFor(visitId);
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
