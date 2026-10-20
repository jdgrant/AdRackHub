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
    private readonly ToteLabelPdfGenerator _toteLabelPdf;
    private readonly BrochureWarehouseSheetService _warehouseSheet;
    private readonly BrochureInventoryReportService _inventoryReport;

    public ReportsController(
        ApplicationDbContext context,
        RouteCustomerReportPdfGenerator routeCustomerReportPdf,
        BrochureLabelPdfGenerator brochureLabelPdf,
        ToteLabelPdfGenerator toteLabelPdf,
        BrochureWarehouseSheetService warehouseSheet,
        BrochureInventoryReportService inventoryReport)
    {
        _context = context;
        _routeCustomerReportPdf = routeCustomerReportPdf;
        _brochureLabelPdf = brochureLabelPdf;
        _toteLabelPdf = toteLabelPdf;
        _warehouseSheet = warehouseSheet;
        _inventoryReport = inventoryReport;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var labelClients = await LoadLabelClientsAsync(cancellationToken);
        var model = new ReportsIndexViewModel
        {
            LabelClients = labelClients,
            LabelRoutes = await LoadLabelRoutesAsync(labelClients, cancellationToken)
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

    [HttpGet]
    public IActionResult ToteLabelsPdf()
    {
        TempData["Error"] = "Select one or more clients to print tote labels.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToteLabelsPdf(int[]? customerIds, CancellationToken cancellationToken)
    {
        var ids = customerIds?.Where(id => id > 0).Distinct().ToArray() ?? Array.Empty<int>();
        if (ids.Length == 0)
        {
            TempData["Error"] = "Select one or more clients to print tote labels.";
            return RedirectToAction(nameof(Index));
        }

        var pdf = await _toteLabelPdf.BuildAsync(ids, cancellationToken);
        return File(pdf, "application/pdf", $"Tote-labels-{DateTime.Today:yyyyMMdd}.pdf");
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
                LastScanAt = c.BrochureScans.Max(s => (DateTime?)s.UploadedAt),
                Routes = c.CustomerRoutes
                    .Where(cr => cr.Status == CustomerRouteStatus.Active)
                    .Select(cr => new { cr.RouteId, cr.Route.RouteName })
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
                    : row.LastInventoryAt ?? row.LastScanAt,
                RouteIds = row.Routes.Select(r => r.RouteId).Distinct().ToList(),
                RouteNames = row.Routes
                    .Select(r => r.RouteName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList()
            })
            .OrderByDescending(row => row.LastUpdated.HasValue)
            .ThenByDescending(row => row.LastUpdated)
            .ThenBy(row => row.CustomerName)
            .ToList();
    }

    private async Task<List<BrochureLabelRouteOption>> LoadLabelRoutesAsync(
        IReadOnlyList<BrochureLabelClientRow> clients,
        CancellationToken cancellationToken)
    {
        var routeIds = clients.SelectMany(c => c.RouteIds).Distinct().ToList();
        if (routeIds.Count == 0)
            return new List<BrochureLabelRouteOption>();

        return await _context.Routes
            .AsNoTracking()
            .Where(r => routeIds.Contains(r.Id))
            .OrderBy(r => r.RouteName)
            .Select(r => new BrochureLabelRouteOption
            {
                Id = r.Id,
                RouteName = r.RouteName
            })
            .ToListAsync(cancellationToken);
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
