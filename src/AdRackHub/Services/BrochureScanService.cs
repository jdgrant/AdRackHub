using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class BrochureScanService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp"
    };

    public const long MaxFileSizeBytes = 20 * 1024 * 1024;

    private readonly ApplicationDbContext _context;
    private readonly string _uploadRoot;

    public BrochureScanService(ApplicationDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _uploadRoot = GetUploadRoot(environment);
    }

    public static string GetUploadRoot(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", "brochures");

    public static string? GetLegacyUploadRoot(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "Data", "Uploads", "brochures");

    public async Task<CustomerBrochureScan> SaveAsync(
        int customerId,
        IFormFile file,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
            throw new InvalidOperationException("Choose a file to upload.");

        if (file.Length > MaxFileSizeBytes)
            throw new InvalidOperationException("Brochure scans must be 20 MB or smaller.");

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Upload a PDF or image file (PDF, JPG, PNG, GIF, or WEBP).");

        var customerExists = await _context.Customers.AnyAsync(c => c.Id == customerId, cancellationToken);
        if (!customerExists)
            throw new InvalidOperationException("Customer not found.");

        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var customerDirectory = Path.Combine(_uploadRoot, customerId.ToString());

        try
        {
            Directory.CreateDirectory(customerDirectory);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not create upload folder. Ensure the app can write to App_Data/Uploads on the server. ({ex.Message})");
        }

        var fullPath = Path.Combine(customerDirectory, storedFileName);
        try
        {
            await using (var stream = File.Create(fullPath))
                await file.CopyToAsync(stream, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not save the uploaded file. Check write permissions for App_Data/Uploads on the server. ({ex.Message})");
        }

        var scan = new CustomerBrochureScan
        {
            CustomerId = customerId,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = storedFileName,
            ContentType = ContentTypes.GetValueOrDefault(extension, file.ContentType),
            FileSizeBytes = file.Length,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            UploadedAt = DateTime.UtcNow
        };

        _context.CustomerBrochureScans.Add(scan);
        await _context.SaveChangesAsync(cancellationToken);
        return scan;
    }

    public async Task<CustomerBrochureScan> SaveFromPathAsync(
        int customerId,
        string sourceFilePath,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Brochure file not found.", sourceFilePath);

        var fileInfo = new FileInfo(sourceFilePath);
        if (fileInfo.Length == 0)
            throw new InvalidOperationException("Brochure file is empty.");

        if (fileInfo.Length > MaxFileSizeBytes)
            throw new InvalidOperationException("Brochure scans must be 20 MB or smaller.");

        var extension = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Upload a PDF or image file (PDF, JPG, PNG, GIF, or WEBP).");

        var customerExists = await _context.Customers.AnyAsync(c => c.Id == customerId, cancellationToken);
        if (!customerExists)
            throw new InvalidOperationException("Customer not found.");

        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var customerDirectory = Path.Combine(_uploadRoot, customerId.ToString());
        Directory.CreateDirectory(customerDirectory);

        var fullPath = Path.Combine(customerDirectory, storedFileName);
        await using (var source = File.OpenRead(sourceFilePath))
        await using (var destination = File.Create(fullPath))
            await source.CopyToAsync(destination, cancellationToken);

        var scan = new CustomerBrochureScan
        {
            CustomerId = customerId,
            OriginalFileName = Path.GetFileName(sourceFilePath),
            StoredFileName = storedFileName,
            ContentType = ContentTypes.GetValueOrDefault(extension, "application/octet-stream"),
            FileSizeBytes = fileInfo.Length,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            UploadedAt = DateTime.UtcNow
        };

        _context.CustomerBrochureScans.Add(scan);
        await _context.SaveChangesAsync(cancellationToken);
        return scan;
    }

    public async Task<CustomerBrochureScan?> GetAsync(int scanId, CancellationToken cancellationToken = default) =>
        await _context.CustomerBrochureScans.FirstOrDefaultAsync(s => s.Id == scanId, cancellationToken);

    public string? ResolveFilePath(CustomerBrochureScan scan, IWebHostEnvironment environment)
    {
        var primary = Path.Combine(GetUploadRoot(environment), scan.CustomerId.ToString(), scan.StoredFileName);
        if (File.Exists(primary))
            return primary;

        var legacyRoot = GetLegacyUploadRoot(environment);
        if (legacyRoot == null)
            return null;

        var legacy = Path.Combine(legacyRoot, scan.CustomerId.ToString(), scan.StoredFileName);
        return File.Exists(legacy) ? legacy : null;
    }

    public string GetFilePath(CustomerBrochureScan scan) =>
        Path.Combine(_uploadRoot, scan.CustomerId.ToString(), scan.StoredFileName);

    public async Task DeleteAsync(int scanId, CancellationToken cancellationToken = default)
    {
        var scan = await _context.CustomerBrochureScans.FirstOrDefaultAsync(s => s.Id == scanId, cancellationToken)
            ?? throw new InvalidOperationException("Brochure scan not found.");

        var filePath = GetFilePath(scan);
        if (File.Exists(filePath))
            File.Delete(filePath);

        _context.CustomerBrochureScans.Remove(scan);
        await _context.SaveChangesAsync(cancellationToken);

        var customerDirectory = Path.Combine(_uploadRoot, scan.CustomerId.ToString());
        if (Directory.Exists(customerDirectory) && !Directory.EnumerateFileSystemEntries(customerDirectory).Any())
            Directory.Delete(customerDirectory);
    }
}
