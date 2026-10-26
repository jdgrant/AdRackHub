using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdRackHub.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[Route("Admin/Communications")]
public class CommunicationsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly InvoiceReminderTemplateService _templates;
    private readonly InvoicePdfStorageService _pdfs;
    private readonly MailgunInvoiceEmailService _mailgun;
    private readonly MailgunOptions _mailgunOptions;

    public CommunicationsController(
        ApplicationDbContext context,
        InvoiceReminderTemplateService templates,
        InvoicePdfStorageService pdfs,
        MailgunInvoiceEmailService mailgun,
        IOptions<MailgunOptions> mailgunOptions)
    {
        _context = context;
        _templates = templates;
        _pdfs = pdfs;
        _mailgun = mailgun;
        _mailgunOptions = mailgunOptions.Value;
    }

    [HttpGet("InvoiceReminder")]
    public async Task<IActionResult> InvoiceReminder(CancellationToken cancellationToken)
    {
        var current = await _templates.GetCurrentAsync(cancellationToken);
        return View(await DecorateAsync(new InvoiceReminderTemplatePageViewModel
        {
            Subject = current.Subject,
            Body = current.Body
        }, current, cancellationToken));
    }

    [HttpPost("InvoiceReminder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveInvoiceReminder(
        InvoiceReminderTemplatePageViewModel model,
        CancellationToken cancellationToken)
    {
        var result = await _templates.SaveAsync(
            model.Subject,
            model.Body,
            CurrentUserLabel(),
            InvoiceReminderMerge.SavedChange,
            cancellationToken);
        if (result.Error != null)
        {
            ModelState.AddModelError(string.Empty, result.Error);
            return View("InvoiceReminder", await DecoratePostedAsync(model, cancellationToken));
        }

        TempData["Message"] = result.Saved
            ? "Saved the invoice reminder template."
            : "No changes to save.";
        return RedirectToAction(nameof(InvoiceReminder));
    }

    [HttpPost("InvoiceReminder/Preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewInvoiceReminder(
        InvoiceReminderTemplatePageViewModel model,
        CancellationToken cancellationToken)
    {
        var errors = InvoiceReminderMerge.Validate(model.Subject, model.Body);
        foreach (var error in errors)
            ModelState.AddModelError(string.Empty, error);

        if (errors.Count == 0)
        {
            var rendered = await _templates.RenderAsync(model.Subject, model.Body, model.PreviewInvoiceId, cancellationToken);
            if (!rendered.Found)
                ModelState.AddModelError(string.Empty, rendered.Error ?? "That invoice was not found.");
            else
            {
                model.HasPreview = true;
                model.PreviewSubject = rendered.Subject;
                model.PreviewBody = rendered.Body;
                model.PreviewSourceLabel = rendered.SourceLabel;
            }
        }

        return View("InvoiceReminder", await DecoratePostedAsync(model, cancellationToken));
    }

    [HttpPost("InvoiceReminder/Test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendInvoiceReminderTest(
        InvoiceReminderTemplatePageViewModel model,
        CancellationToken cancellationToken)
    {
        var errors = InvoiceReminderMerge.Validate(model.Subject, model.Body);
        foreach (var error in errors)
            ModelState.AddModelError(string.Empty, error);
        if (errors.Count > 0)
            return View("InvoiceReminder", await DecoratePostedAsync(model, cancellationToken));

        var rendered = await _templates.RenderAsync(model.Subject, model.Body, model.PreviewInvoiceId, cancellationToken);
        if (!rendered.Found)
        {
            ModelState.AddModelError(string.Empty, rendered.Error ?? "That invoice was not found.");
            return View("InvoiceReminder", await DecoratePostedAsync(model, cancellationToken));
        }

        model.HasPreview = true;
        model.PreviewSubject = rendered.Subject;
        model.PreviewBody = rendered.Body;
        model.PreviewSourceLabel = rendered.SourceLabel;

        var pdf = await InvoiceReminderPdf.TryReadAsync(_context, _pdfs, model.PreviewInvoiceId, cancellationToken);
        var sent = await _mailgun.SendReminderAsync(
            [InvoiceReminderMerge.TestRecipient],
            rendered.Subject,
            rendered.Body,
            pdf.Bytes,
            pdf.FileName,
            cancellationToken);
        if (!sent.Success)
        {
            TempData["Error"] = sent.ErrorMessage ?? "The test email could not be sent.";
            return View("InvoiceReminder", await DecoratePostedAsync(model, cancellationToken));
        }

        TempData["Message"] = $"Sent a test reminder to {InvoiceReminderMerge.TestRecipient}. The saved template was not changed.";
        return View("InvoiceReminder", await DecoratePostedAsync(model, cancellationToken));
    }

    [HttpPost("InvoiceReminder/Restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreInvoiceReminder(CancellationToken cancellationToken)
    {
        var result = await _templates.RestoreDefaultAsync(CurrentUserLabel(), cancellationToken);
        if (result.Error != null)
            TempData["Error"] = result.Error;
        else if (!result.Saved)
            TempData["Message"] = "The template is already the original system default.";
        else
            TempData["Message"] = "Restored the original invoice reminder template.";
        return RedirectToAction(nameof(InvoiceReminder));
    }

    private async Task<InvoiceReminderTemplatePageViewModel> DecoratePostedAsync(
        InvoiceReminderTemplatePageViewModel model,
        CancellationToken cancellationToken)
    {
        var current = await _templates.GetCurrentAsync(cancellationToken);
        model.Subject = InvoiceReminderMerge.Normalize(model.Subject);
        model.Body = InvoiceReminderMerge.Normalize(model.Body);
        return await DecorateAsync(model, current, cancellationToken);
    }

    private async Task<InvoiceReminderTemplatePageViewModel> DecorateAsync(
        InvoiceReminderTemplatePageViewModel model,
        InvoiceReminderTemplate current,
        CancellationToken cancellationToken)
    {
        model.MergeFields = InvoiceReminderMerge.Fields;
        model.PreviewChoices = await _templates.ListPreviewChoicesAsync(cancellationToken);
        model.History = await _templates.ListHistoryAsync(cancellationToken);
        model.MailgunConfigured = _mailgunOptions.IsConfigured;
        model.TestRecipient = InvoiceReminderMerge.TestRecipient;
        model.LogoUrl = string.IsNullOrWhiteSpace(_mailgunOptions.LogoUrl)
            ? "https://ad-rack.com/wp-content/uploads/2020/10/Ad-Rack-logo-lg.png"
            : _mailgunOptions.LogoUrl.Trim();
        model.MatchesSaved = InvoiceReminderMerge.Normalize(model.Subject) == InvoiceReminderMerge.Normalize(current.Subject)
            && InvoiceReminderMerge.Normalize(model.Body) == InvoiceReminderMerge.Normalize(current.Body);
        model.IsSystemDefault = InvoiceReminderMerge.Normalize(current.Subject) == InvoiceReminderMerge.Normalize(InvoiceReminderMerge.DefaultSubject)
            && InvoiceReminderMerge.Normalize(current.Body) == InvoiceReminderMerge.Normalize(InvoiceReminderMerge.DefaultBody)
            && current.UpdatedBy == null;
        model.UpdatedLabel = current.UpdatedBy == null
            ? "This is the original system default."
            : $"Last saved by {current.UpdatedBy} on {InvoiceReminderMerge.FormatStamp(current.UpdatedAt)}.";
        return model;
    }

    private string CurrentUserLabel() =>
        User.Identity?.Name ?? "Admin";
}
