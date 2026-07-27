using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Customers)]
public class BillingController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly MonthlyBillingService _billingService;
    private readonly WaveApiService _waveApiService;

    public BillingController(
        ApplicationDbContext context,
        MonthlyBillingService billingService,
        WaveApiService waveApiService)
    {
        _context = context;
        _billingService = billingService;
        _waveApiService = waveApiService;
    }

    public async Task<IActionResult> Index(int? year, int? month)
    {
        var now = DateTime.Today;
        var selectedYear = year ?? now.Year;
        var selectedMonth = month ?? now.Month;

        var model = new BillingPageViewModel
        {
            Year = selectedYear,
            Month = selectedMonth,
            PeriodLabel = BillingDueCalculator.PeriodLabel(selectedYear, selectedMonth),
            DueContracts = await _billingService.GetDueContractsAsync(selectedYear, selectedMonth),
            AllContracts = await _billingService.GetAllConfiguredContractsAsync(selectedYear, selectedMonth),
            Run = await _billingService.GetRunAsync(selectedYear, selectedMonth),
            WaveConfigured = _waveApiService.IsConfigured
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Prepare(int year, int month)
    {
        try
        {
            var run = await _billingService.PrepareRunAsync(year, month);
            TempData["Message"] = $"Prepared {run.Invoices.Count} invoices for {BillingDueCalculator.PeriodLabel(year, month)}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToWave(int year, int month)
    {
        try
        {
            var run = await _billingService.GetRunAsync(year, month);
            if (run == null)
                run = await _billingService.PrepareRunAsync(year, month);

            run = await _billingService.SubmitRunAsync(run.Id);
            TempData["Message"] = run.Status switch
            {
                BillingRunStatus.Submitted => $"Sent {run.Invoices.Count(i => i.Status == BillingRunInvoiceStatus.Submitted)} invoice(s) to Wave.",
                BillingRunStatus.PartiallySubmitted => "Some invoices were sent to Wave. Retry failed invoices below.",
                BillingRunStatus.Failed => "Wave submission failed. Check errors below and retry.",
                _ => "Billing updated."
            };
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(int year, int month)
    {
        var run = await _billingService.GetRunAsync(year, month);
        if (run == null)
            return RedirectToAction(nameof(Index), new { year, month });

        try
        {
            run = await _billingService.SubmitRunAsync(run.Id);
            TempData["Message"] = run.Status switch
            {
                BillingRunStatus.Submitted => "All remaining invoices sent to Wave.",
                BillingRunStatus.PartiallySubmitted => "Some invoices still failed. Review errors below.",
                _ => "Retry completed."
            };
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { year, month });
    }
}
