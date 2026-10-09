using AdRackHub.Models;
using AdRackHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdRackHub.Controllers;

[Authorize(Roles = $"{AppRoles.Customers},{AppRoles.RoutesStops},{AppRoles.Admin}")]
public class ReportsController : Controller
{
    private readonly RouteCustomerReportPdfGenerator _routeCustomerReportPdf;
    private readonly BrochureLabelPdfGenerator _brochureLabelPdf;
    private readonly BrochureWarehouseSheetService _warehouseSheet;
    private readonly BrochureInventoryReportService _inventoryReport;

    public ReportsController(
        RouteCustomerReportPdfGenerator routeCustomerReportPdf,
        BrochureLabelPdfGenerator brochureLabelPdf,
        BrochureWarehouseSheetService warehouseSheet,
        BrochureInventoryReportService inventoryReport)
    {
        _routeCustomerReportPdf = routeCustomerReportPdf;
        _brochureLabelPdf = brochureLabelPdf;
        _warehouseSheet = warehouseSheet;
        _inventoryReport = inventoryReport;
    }

    public IActionResult Index() => View();

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

    public async Task<IActionResult> BrochureLabelsPdf(CancellationToken cancellationToken)
    {
        var pdf = await _brochureLabelPdf.BuildAsync(cancellationToken);
        return File(pdf, "application/pdf", $"Brochure-labels-nametags-{DateTime.Today:yyyyMMdd}.pdf");
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
