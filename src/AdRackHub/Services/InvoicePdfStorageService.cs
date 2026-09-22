using Microsoft.AspNetCore.Hosting;
using PDFtoImage;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdRackHub.Services;

public class InvoicePdfStorageService
{
    private readonly string _uploadRoot;

    public InvoicePdfStorageService(IWebHostEnvironment environment)
    {
        _uploadRoot = GetUploadRoot(environment);
        Directory.CreateDirectory(_uploadRoot);
    }

    public static string GetUploadRoot(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", "invoices");

    public string Save(int contractId, string? invoiceNumber, byte[] bytes, string? previousStoredFileName = null)
    {
        if (bytes == null || bytes.Length == 0)
            throw new InvalidOperationException("Invoice PDF bytes were empty.");

        var fileName = BuildFileName(contractId, invoiceNumber);
        File.WriteAllBytes(Path.Combine(_uploadRoot, fileName), bytes);

        var legacyName = BuildFileName(invoiceNumber);
        if (!string.Equals(legacyName, fileName, StringComparison.OrdinalIgnoreCase)
            && !IsSampleFile(legacyName))
        {
            File.WriteAllBytes(Path.Combine(_uploadRoot, legacyName), bytes);
        }

        _ = previousStoredFileName;
        return fileName;
    }

    public void DeleteReplaced(string? previousStoredFileName, string currentFileName)
    {
        if (string.IsNullOrWhiteSpace(previousStoredFileName))
            return;

        var previous = Path.GetFileName(previousStoredFileName);
        if (string.IsNullOrWhiteSpace(previous)
            || IsSampleFile(previous)
            || string.Equals(previous, Path.GetFileName(currentFileName), StringComparison.OrdinalIgnoreCase))
            return;

        var path = Path.Combine(_uploadRoot, previous);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static string BuildFileName(string? invoiceNumber) =>
        $"AdRack-{SanitizeFileName(invoiceNumber)}.pdf";

    public static string BuildFileName(int contractId, string? invoiceNumber) =>
        $"AdRack-{contractId}-{SanitizeFileName(invoiceNumber)}.pdf";

    public string? ResolveFilePath(string? storedFileName, string? invoiceNumber = null, int? contractId = null)
    {
        foreach (var candidate in CandidatePaths(storedFileName, invoiceNumber, contractId))
        {
            if (!File.Exists(candidate))
                continue;

            if (contractId is > 0 && !string.IsNullOrWhiteSpace(invoiceNumber))
            {
                var scoped = Path.Combine(_uploadRoot, BuildFileName(contractId.Value, invoiceNumber));
                if (!string.Equals(candidate, scoped, StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(scoped))
                {
                    try
                    {
                        File.Copy(candidate, scoped);
                        return scoped;
                    }
                    catch
                    {
                        return candidate;
                    }
                }
            }

            return candidate;
        }

        return null;
    }

    public static string DownloadFileName(string? invoiceNumber, string? storedFileName)
    {
        if (!string.IsNullOrWhiteSpace(invoiceNumber))
            return BuildFileName(invoiceNumber);
        if (!string.IsNullOrWhiteSpace(storedFileName))
            return Path.GetFileName(storedFileName);
        return BuildFileName(null);
    }

    public byte[] Merge(IReadOnlyList<string> filePaths)
    {
        var existing = (filePaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existing.Count == 0)
            throw new InvalidOperationException("No invoice PDFs to merge.");
        if (existing.Count == 1)
            return File.ReadAllBytes(existing[0]);

        var pageImages = new List<byte[]>();
        var options = new RenderOptions(Dpi: 150);
        foreach (var path in existing)
        {
            using var pdfStream = File.OpenRead(path);
            var pageCount = Conversion.GetPageCount(pdfStream, leaveOpen: true);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                pdfStream.Position = 0;
                var pngPath = Path.Combine(Path.GetTempPath(), $"AdRack-merge-{Guid.NewGuid():N}.png");
                try
                {
                    Conversion.SavePng(pngPath, pdfStream, page: pageIndex, leaveOpen: true, options: options);
                    pageImages.Add(File.ReadAllBytes(pngPath));
                }
                finally
                {
                    if (File.Exists(pngPath))
                        File.Delete(pngPath);
                }
            }
        }

        if (pageImages.Count == 0)
            throw new InvalidOperationException("No invoice PDF pages to merge.");

        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container =>
        {
            foreach (var image in pageImages)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(0);
                    page.Content().Image(image).FitArea();
                });
            }
        }).GeneratePdf();
    }

    private IEnumerable<string> CandidatePaths(string? storedFileName, string? invoiceNumber, int? contractId)
    {
        if (!string.IsNullOrWhiteSpace(storedFileName))
        {
            var name = Path.GetFileName(storedFileName);
            if (!string.IsNullOrWhiteSpace(name))
                yield return Path.Combine(_uploadRoot, name);
        }

        if (contractId is > 0 && !string.IsNullOrWhiteSpace(invoiceNumber))
            yield return Path.Combine(_uploadRoot, BuildFileName(contractId.Value, invoiceNumber));

        if (!string.IsNullOrWhiteSpace(invoiceNumber))
            yield return Path.Combine(_uploadRoot, BuildFileName(invoiceNumber));

        if (contractId is > 0)
        {
            var matches = Directory.GetFiles(_uploadRoot, $"AdRack-{contractId.Value}-*.pdf")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
            foreach (var match in matches)
                yield return match;
        }
    }

    private static bool IsSampleFile(string fileName) =>
        fileName.StartsWith("AdRack-sample", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeFileName(string? invoiceNumber)
    {
        var raw = string.IsNullOrWhiteSpace(invoiceNumber) ? "invoice" : invoiceNumber.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            raw = raw.Replace(c, '-');
        return string.IsNullOrWhiteSpace(raw) ? "invoice" : raw;
    }
}
