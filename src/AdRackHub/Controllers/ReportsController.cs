using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Roles = $"{AppRoles.Customers},{AppRoles.RoutesStops},{AppRoles.Admin}")]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly RouteCustomerReportPdfGenerator _routeCustomerReportPdf;
    private readonly BrochureLabelPdfGenerator _brochureLabelPdf;
    private readonly BrochureWarehouseSheetService _warehouseSheet;
    private readonly BrochureInventoryReportService _inventoryReport;

    public ReportsController(
        ApplicationDbContext context,
        RouteCustomerReportPdfGenerator routeCustomerReportPdf,
        BrochureLabelPdfGenerator brochureLabelPdf,
        BrochureWarehouseSheetService warehouseSheet,
        BrochureInventoryReportService inventoryReport)
    {
        _context = context;
        _routeCustomerReportPdf = routeCustomerReportPdf;
        _brochureLabelPdf = brochureLabelPdf;
        _warehouseSheet = warehouseSheet;
        _inventoryReport = inventoryReport;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new ReportsIndexViewModel
        {
            LabelClients = await LoadLabelClientsAsync(cancellationToken)
        };
        return View(model);
    }

    public async Task<IActionResult> Inventory(CancellationToken cancellationToken)
    {
        var report = await _inventoryReport.BuildAsync(cancellationToken);
        return View(report);
    }

    public async Task<IActionResult> HotelCustomersPdf(CancellationToken cancellationToken)
    {
        var pdf = await _routeCustomerReportPdf.BuildHotelAsync(cancellationToken);
        return File(pdf, "application/pdf", $"Hotel-customers-by-route-{DateTime.Today:yyyyMMdd}.pdf");
    }

    public async Task<IActionResult> RestAreaCustomersPdf(CancellationToken cancellationToken)
    {
        var pdf = await _routeCustomerReportPdf.BuildRestAreaAsync(cancellationToken);
        return File(pdf, "application/pdf", $"Rest-area-customers-by-location-{DateTime.Today:yyyyMMdd}.pdf");
    }

    [HttpGet]
    public IActionResult BrochureLabelsPdf()
    {
        TempData["Error"] = "Select one or more clients to print warehouse labels.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BrochureLabelsPdf(int[]? customerIds, CancellationToken cancellationToken)
    {
        var ids = customerIds?.Where(id => id > 0).Distinct().ToArray() ?? Array.Empty<int>();
        if (ids.Length == 0)
        {
            TempData["Error"] = "Select one or more clients to print warehouse labels.";
            return RedirectToAction(nameof(Index));
        }

        var pdf = await _brochureLabelPdf.BuildAsync(ids, cancellationToken);
        return File(pdf, "application/pdf", $"Brochure-labels-nametags-{DateTime.Today:yyyyMMdd}.pdf");
    }

    private async Task<List<BrochureLabelClientRow>> LoadLabelClientsAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.Customers
            .AsNoTracking()
            .Where(c => c.Type == CustomerType.Customer && c.Status == CustomerStatus.Active)
            .Select(c => new
            {
                c.Id,
                c.CustomerName,
                c.BrochureCode,
                LastInventoryAt = c.BrochureInventories.Max(i => (DateTime?)i.CreatedAt),
                LastScanAt = c.BrochureScans.Max(s => (DateTime?)s.UploadedAt)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new BrochureLabelClientRow
            {
                Id = row.Id,
                CustomerName = row.CustomerName,
                BrochureCode = row.BrochureCode,
                LastUpdated = row.LastInventoryAt.HasValue && row.LastScanAt.HasValue
                    ? (row.LastInventoryAt > row.LastScanAt ? row.LastInventoryAt : row.LastScanAt)
                    : row.LastInventoryAt ?? row.LastScanAt
            })
            .OrderByDescending(row => row.LastUpdated.HasValue)
            .ThenByDescending(row => row.LastUpdated)
            .ThenBy(row => row.CustomerName)
            .ToList();
    }

    public async Task<IActionResult> WarehouseSheet(CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();
        await _warehouseSheet.ExportAsync(stream, cancellationToken);
        stream.Position = 0;
        return File(
            stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            BrochureWarehouseSheetService.FileName);
    }

    public async Task<IActionResult> WarehouseInventoryPdf(string? warehouse, CancellationToken cancellationToken)
    {
        var report = await _inventoryReport.BuildAsync(cancellationToken);
        var pdf = _inventoryReport.GeneratePdf(report, warehouse);
        var suffix = string.IsNullOrWhiteSpace(warehouse)
            || string.Equals(warehouse, "all", StringComparison.OrdinalIgnoreCase)
            ? "all"
            : string.Equals(warehouse, "O", StringComparison.OrdinalIgnoreCase)
                ? "ohio"
                : BrochureInventoryReportService.IsUnassignedCode(warehouse)
                    ? "unassigned"
                    : "kentucky";
        return File(pdf, "application/pdf", $"warehouse-inventory-count-sheet-{suffix}-{report.AsOf:yyyyMMdd}.pdf");
    }
}
