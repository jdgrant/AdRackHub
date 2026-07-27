using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace AdRackHub.Services;

public class BrochureOptimizeResult
{
    public int ImageScansFound { get; set; }
    public int Optimized { get; set; }
    public int SkippedAlreadyOptimized { get; set; }
    public int SkippedMissingFile { get; set; }
    public int SkippedPdf { get; set; }
    public int Failed { get; set; }
    public long BytesBefore { get; set; }
    public long BytesAfter { get; set; }
    public List<string> Errors { get; set; } = new();

    public long BytesSaved => Math.Max(0, BytesBefore - BytesAfter);
}

public class BrochureOptimizeService
{
    public const int MaxLongEdgePixels = 1600;
    public const int JpegQuality = 80;
    public const long SkipIfSmallerThanBytes = 400 * 1024;

    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<BrochureOptimizeService> _logger;

    public BrochureOptimizeService(
        ApplicationDbContext context,
        IWebHostEnvironment environment,
        ILogger<BrochureOptimizeService> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    public async Task<BrochureOptimizeStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var scans = await _context.CustomerBrochureScans.AsNoTracking().ToListAsync(cancellationToken);
        var images = scans.Where(s => s.IsImage).ToList();
        long onDiskBytes = 0;
        var missing = 0;

        foreach (var scan in images)
        {
            var path = ResolveExistingPath(scan);
            if (path == null)
            {
                missing++;
                continue;
            }

            onDiskBytes += new FileInfo(path).Length;
        }

        return new BrochureOptimizeStats
        {
            TotalScans = scans.Count,
            ImageScans = images.Count,
            PdfScans = scans.Count(s => !s.IsImage),
            MissingFiles = missing,
            TotalImageBytesOnDisk = onDiskBytes,
            MaxLongEdgePixels = MaxLongEdgePixels,
            JpegQuality = JpegQuality
        };
    }

    public async Task<BrochureOptimizeResult> OptimizeAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new BrochureOptimizeResult();
        var scans = await _context.CustomerBrochureScans.ToListAsync(cancellationToken);
        result.SkippedPdf = scans.Count(s => !s.IsImage);

        var images = scans.Where(s => s.IsImage).ToList();
        result.ImageScansFound = images.Count;

        foreach (var scan in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var outcome = await OptimizeOneAsync(scan, cancellationToken);
                switch (outcome.Status)
                {
                    case OptimizeStatus.Optimized:
                        result.Optimized++;
                        result.BytesBefore += outcome.BytesBefore;
                        result.BytesAfter += outcome.BytesAfter;
                        break;
                    case OptimizeStatus.SkippedAlreadyOptimized:
                        result.SkippedAlreadyOptimized++;
                        result.BytesBefore += outcome.BytesBefore;
                        result.BytesAfter += outcome.BytesAfter;
                        break;
                    case OptimizeStatus.MissingFile:
                        result.SkippedMissingFile++;
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                var message = $"{scan.OriginalFileName} (#{scan.Id}): {ex.Message}";
                result.Errors.Add(message);
                _logger.LogWarning(ex, "Failed optimizing brochure scan {ScanId}", scan.Id);
            }
        }

        if (result.Optimized > 0)
            await _context.SaveChangesAsync(cancellationToken);

        return result;
    }

    private async Task<OptimizeOneResult> OptimizeOneAsync(CustomerBrochureScan scan, CancellationToken cancellationToken)
    {
        var path = ResolveExistingPath(scan);
        if (path == null)
            return new OptimizeOneResult(OptimizeStatus.MissingFile, 0, 0);

        var originalInfo = new FileInfo(path);
        var bytesBefore = originalInfo.Length;

        Image image;
        await using (var input = File.OpenRead(path))
        {
            image = await Image.LoadAsync(input, cancellationToken);
        }

        using (image)
        {
            var longEdge = Math.Max(image.Width, image.Height);
            var needsResize = longEdge > MaxLongEdgePixels;
            var needsReencode = !IsJpeg(scan) || bytesBefore > SkipIfSmallerThanBytes || needsResize;

            if (!needsResize && bytesBefore <= SkipIfSmallerThanBytes && IsJpeg(scan))
                return new OptimizeOneResult(OptimizeStatus.SkippedAlreadyOptimized, bytesBefore, bytesBefore);

            if (needsResize)
            {
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxLongEdgePixels, MaxLongEdgePixels)
                }));
            }
            else if (!needsReencode)
            {
                return new OptimizeOneResult(OptimizeStatus.SkippedAlreadyOptimized, bytesBefore, bytesBefore);
            }

            var targetPath = path;
            var extension = Path.GetExtension(path);
            var convertingToJpeg = !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

            if (convertingToJpeg)
            {
                var newFileName = Path.ChangeExtension(scan.StoredFileName, ".jpg");
                targetPath = Path.Combine(Path.GetDirectoryName(path)!, newFileName);
            }

            var encoder = new JpegEncoder { Quality = JpegQuality };
            var tempPath = targetPath + ".tmp";
            await image.SaveAsync(tempPath, encoder, cancellationToken);

            var bytesAfter = new FileInfo(tempPath).Length;
            if (bytesAfter >= bytesBefore && !needsResize && !convertingToJpeg)
            {
                File.Delete(tempPath);
                return new OptimizeOneResult(OptimizeStatus.SkippedAlreadyOptimized, bytesBefore, bytesBefore);
            }

            if (File.Exists(targetPath))
                File.Delete(targetPath);
            File.Move(tempPath, targetPath);

            if (convertingToJpeg && !string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                File.Delete(path);

            if (convertingToJpeg)
            {
                scan.StoredFileName = Path.GetFileName(targetPath);
                scan.ContentType = "image/jpeg";
            }

            scan.FileSizeBytes = bytesAfter;
            return new OptimizeOneResult(OptimizeStatus.Optimized, bytesBefore, bytesAfter);
        }
    }

    private static bool IsJpeg(CustomerBrochureScan scan) =>
        scan.ContentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
        || scan.StoredFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || scan.StoredFileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private string? ResolveExistingPath(CustomerBrochureScan scan)
    {
        var primary = Path.Combine(BrochureScanService.GetUploadRoot(_environment), scan.CustomerId.ToString(), scan.StoredFileName);
        if (File.Exists(primary))
            return primary;

        var legacyRoot = BrochureScanService.GetLegacyUploadRoot(_environment);
        if (legacyRoot == null)
            return null;

        var legacy = Path.Combine(legacyRoot, scan.CustomerId.ToString(), scan.StoredFileName);
        return File.Exists(legacy) ? legacy : null;
    }

    private enum OptimizeStatus
    {
        Optimized,
        SkippedAlreadyOptimized,
        MissingFile
    }

    private sealed record OptimizeOneResult(OptimizeStatus Status, long BytesBefore, long BytesAfter);
}

public class BrochureOptimizeStats
{
    public int TotalScans { get; set; }
    public int ImageScans { get; set; }
    public int PdfScans { get; set; }
    public int MissingFiles { get; set; }
    public long TotalImageBytesOnDisk { get; set; }
    public int MaxLongEdgePixels { get; set; }
    public int JpegQuality { get; set; }
}
