using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public interface IInvoiceReminderReader
{
    Task<AgentCallResult<AgentInvoiceReminderReadDto>> ReadAsync(
        int? invoiceId,
        bool canUpdate,
        string hubBase,
        CancellationToken cancellationToken = default);
}

public sealed class InvoiceReminderSaveResult
{
    public bool Saved { get; init; }
    public string? Error { get; init; }

    public static InvoiceReminderSaveResult Ok() => new() { Saved = true };
    public static InvoiceReminderSaveResult Unchanged() => new() { Saved = false };
    public static InvoiceReminderSaveResult Invalid(string error) => new() { Error = error };
}

public class InvoiceReminderTemplateService : IInvoiceReminderReader
{
    private readonly ApplicationDbContext _context;

    public InvoiceReminderTemplateService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InvoiceReminderTemplate> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var current = await _context.InvoiceReminderTemplates
            .OrderBy(template => template.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (current != null)
            return current;

        current = new InvoiceReminderTemplate
        {
            Subject = InvoiceReminderMerge.DefaultSubject,
            Body = InvoiceReminderMerge.DefaultBody,
            UpdatedAt = DateTime.UtcNow
        };
        _context.InvoiceReminderTemplates.Add(current);
        await _context.SaveChangesAsync(cancellationToken);
        return current;
    }

    public async Task<IReadOnlyList<InvoiceReminderRevisionRow>> ListHistoryAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.InvoiceReminderTemplateRevisions
            .AsNoTracking()
            .OrderByDescending(revision => revision.ChangedAt)
            .ThenByDescending(revision => revision.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        return rows.Select(revision => new InvoiceReminderRevisionRow
        {
            When = InvoiceReminderMerge.FormatStamp(revision.ChangedAt),
            Who = string.IsNullOrWhiteSpace(revision.ChangedBy) ? "Unknown" : revision.ChangedBy,
            Change = revision.ChangeKind,
            Subject = revision.Subject,
            Body = revision.Body
        }).ToList();
    }

    public async Task<IReadOnlyList<InvoiceReminderPreviewChoice>> ListPreviewChoicesAsync(CancellationToken cancellationToken = default)
    {
        var invoices = await OutstandingQuery()
            .OrderBy(invoice => invoice.Customer.CustomerName)
            .ThenByDescending(invoice => invoice.BillingRun.Year)
            .ThenByDescending(invoice => invoice.BillingRun.Month)
            .ThenByDescending(invoice => invoice.Id)
            .Take(400)
            .ToListAsync(cancellationToken);

        return invoices.Select(invoice =>
        {
            var data = InvoiceReminderMerge.FromInvoice(invoice);
            return new InvoiceReminderPreviewChoice
            {
                InvoiceId = invoice.Id,
                Label = data.SourceLabel
            };
        }).ToList();
    }

    public async Task<InvoiceReminderSaveResult> SaveAsync(
        string? subject,
        string? body,
        string changedBy,
        string changeKind,
        CancellationToken cancellationToken = default)
    {
        var errors = InvoiceReminderMerge.Validate(subject, body);
        if (errors.Count > 0)
            return InvoiceReminderSaveResult.Invalid(errors[0]);

        var cleanSubject = InvoiceReminderMerge.Normalize(subject);
        var cleanBody = InvoiceReminderMerge.Normalize(body);
        var current = await GetCurrentAsync(cancellationToken);
        if (InvoiceReminderMerge.Normalize(current.Subject) == cleanSubject
            && InvoiceReminderMerge.Normalize(current.Body) == cleanBody)
            return InvoiceReminderSaveResult.Unchanged();

        current.Subject = cleanSubject;
        current.Body = cleanBody;
        current.UpdatedAt = DateTime.UtcNow;
        current.UpdatedBy = TrimEditor(changedBy);
        _context.InvoiceReminderTemplateRevisions.Add(new InvoiceReminderTemplateRevision
        {
            Subject = cleanSubject,
            Body = cleanBody,
            ChangeKind = changeKind,
            ChangedAt = current.UpdatedAt,
            ChangedBy = current.UpdatedBy
        });
        await _context.SaveChangesAsync(cancellationToken);
        return InvoiceReminderSaveResult.Ok();
    }

    public Task<InvoiceReminderSaveResult> RestoreDefaultAsync(string changedBy, CancellationToken cancellationToken = default) =>
        SaveAsync(
            InvoiceReminderMerge.DefaultSubject,
            InvoiceReminderMerge.DefaultBody,
            changedBy,
            InvoiceReminderMerge.RestoredChange,
            cancellationToken);

    public async Task<InvoiceReminderRenderResult> RenderAsync(
        string? subject,
        string? body,
        int? invoiceId,
        CancellationToken cancellationToken = default)
    {
        var current = await GetCurrentAsync(cancellationToken);
        var templateSubject = string.IsNullOrWhiteSpace(subject) ? current.Subject : subject;
        var templateBody = string.IsNullOrWhiteSpace(body) ? current.Body : body;
        var data = await MergeDataAsync(invoiceId, cancellationToken);
        if (data == null)
        {
            return new InvoiceReminderRenderResult
            {
                Found = false,
                Error = "That invoice was not found."
            };
        }

        return new InvoiceReminderRenderResult
        {
            Found = true,
            Subject = InvoiceReminderMerge.Apply(templateSubject, data),
            Body = InvoiceReminderMerge.Apply(templateBody, data),
            SourceLabel = data.SourceLabel,
            InvoiceId = invoiceId
        };
    }

    public async Task<AgentCallResult<AgentInvoiceReminderReadDto>> ReadAsync(
        int? invoiceId,
        bool canUpdate,
        string hubBase,
        CancellationToken cancellationToken = default)
    {
        var current = await GetCurrentAsync(cancellationToken);
        var rendered = await RenderAsync(current.Subject, current.Body, invoiceId, cancellationToken);
        if (!rendered.Found)
            return AgentCallResult<AgentInvoiceReminderReadDto>.Missing(rendered.Error ?? "That invoice was not found.");

        return AgentCallResult<AgentInvoiceReminderReadDto>.Ok(ToReadDto(current, rendered, canUpdate, hubBase));
    }

    public async Task<AgentCallResult<AgentInvoiceReminderRenderDto>> RenderForAgentAsync(
        int? invoiceId,
        string? subject,
        string? body,
        string hubBase,
        CancellationToken cancellationToken = default)
    {
        if (subject != null && InvoiceReminderMerge.Normalize(subject).Length == 0)
            return AgentCallResult<AgentInvoiceReminderRenderDto>.Invalid(["Enter a subject."]);
        if (body != null && InvoiceReminderMerge.Normalize(body).Length == 0)
            return AgentCallResult<AgentInvoiceReminderRenderDto>.Invalid(["Enter an email body."]);

        var rendered = await RenderAsync(subject, body, invoiceId, cancellationToken);
        if (!rendered.Found)
            return AgentCallResult<AgentInvoiceReminderRenderDto>.Missing(rendered.Error ?? "That invoice was not found.");

        return AgentCallResult<AgentInvoiceReminderRenderDto>.Ok(new AgentInvoiceReminderRenderDto
        {
            Subject = rendered.Subject,
            Body = rendered.Body,
            Source = invoiceId.HasValue ? "invoice" : "sample",
            InvoiceId = invoiceId,
            CustomerName = CustomerFromLabel(rendered.SourceLabel),
            Saved = false,
            HubUrl = HubUrl(hubBase)
        });
    }

    public async Task<AgentCallResult<AgentInvoiceReminderReadDto>> UpdateForAgentAsync(
        string? subject,
        string? body,
        bool administrativePermission,
        string hubBase,
        CancellationToken cancellationToken = default)
    {
        if (!administrativePermission)
            return AgentCallResult<AgentInvoiceReminderReadDto>.Fail(403, InvoiceReminderMerge.UpdateDeniedMessage);

        var errors = InvoiceReminderMerge.Validate(subject, body);
        if (errors.Count > 0)
            return AgentCallResult<AgentInvoiceReminderReadDto>.Invalid(errors.ToList());

        await SaveAsync(subject, body, InvoiceReminderMerge.AgentEditor, InvoiceReminderMerge.SavedChange, cancellationToken);
        return await ReadAsync(null, true, hubBase, cancellationToken);
    }

    public async Task<BillingRunInvoice?> FindOutstandingAsync(int invoiceId, CancellationToken cancellationToken = default) =>
        await OutstandingQuery().FirstOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken);

    public async Task<BillingRunInvoice?> FindInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default) =>
        await _context.BillingRunInvoices
            .AsNoTracking()
            .Include(invoice => invoice.Customer)
            .Include(invoice => invoice.BillingRun)
            .Include(invoice => invoice.Lines)
            .FirstOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken);

    private async Task<InvoiceReminderMergeData?> MergeDataAsync(int? invoiceId, CancellationToken cancellationToken)
    {
        if (!invoiceId.HasValue)
            return InvoiceReminderMerge.Sample;

        var invoice = await FindInvoiceAsync(invoiceId.Value, cancellationToken);
        return invoice == null ? null : InvoiceReminderMerge.FromInvoice(invoice);
    }

    private IQueryable<BillingRunInvoice> OutstandingQuery() =>
        _context.BillingRunInvoices
            .AsNoTracking()
            .Include(invoice => invoice.Customer)
            .Include(invoice => invoice.BillingRun)
            .Where(invoice => invoice.Status == BillingRunInvoiceStatus.Submitted);

    private static AgentInvoiceReminderReadDto ToReadDto(
        InvoiceReminderTemplate current,
        InvoiceReminderRenderResult rendered,
        bool canUpdate,
        string hubBase) =>
        new()
        {
            Subject = current.Subject,
            Body = current.Body,
            UpdatedAt = current.UpdatedBy == null ? null : current.UpdatedAt.ToString("o"),
            UpdatedBy = current.UpdatedBy,
            CanUpdate = canUpdate,
            RenderedSubject = rendered.Subject,
            RenderedBody = rendered.Body,
            RenderSource = rendered.InvoiceId.HasValue ? "invoice" : "sample",
            InvoiceId = rendered.InvoiceId,
            CustomerName = rendered.InvoiceId.HasValue ? CustomerFromLabel(rendered.SourceLabel) : InvoiceReminderMerge.Sample.CustomerName,
            MergeFields = InvoiceReminderMerge.Fields
                .Select(field => new AgentInvoiceReminderMergeFieldDto { Token = field.Token, Description = field.Description })
                .ToList(),
            HubUrl = HubUrl(hubBase)
        };

    private static string HubUrl(string hubBase) =>
        $"{hubBase.TrimEnd('/')}/Admin/Communications/InvoiceReminder";

    private static string? CustomerFromLabel(string sourceLabel)
    {
        var split = sourceLabel.Split('·', 2, StringSplitOptions.TrimEntries);
        return split.Length > 0 && !string.IsNullOrWhiteSpace(split[0]) ? split[0] : null;
    }

    private static string TrimEditor(string changedBy)
    {
        var editor = changedBy.Trim();
        return editor.Length <= 200 ? editor : editor[..200];
    }
}

public static class InvoiceReminderPdf
{
    public static async Task<(byte[]? Bytes, string? FileName)> TryReadAsync(
        ApplicationDbContext context,
        InvoicePdfStorageService pdfs,
        int? invoiceId,
        CancellationToken cancellationToken)
    {
        if (invoiceId is not > 0)
            return (null, null);

        var invoice = await context.BillingRunInvoices
            .AsNoTracking()
            .Include(item => item.Lines)
            .FirstOrDefaultAsync(item => item.Id == invoiceId.Value, cancellationToken);
        if (invoice == null)
            return (null, null);

        var contractIds = invoice.Lines.Select(line => line.CustomerContractId).Distinct().ToList();
        var contracts = await context.CustomerContracts
            .AsNoTracking()
            .Where(contract => contractIds.Contains(contract.Id))
            .Select(contract => new { contract.Id, contract.WaveInvoicePdfPath, contract.WaveInvoiceNumber })
            .ToListAsync(cancellationToken);

        foreach (var contract in contracts)
        {
            var path = pdfs.ResolveFilePath(contract.WaveInvoicePdfPath, contract.WaveInvoiceNumber, contract.Id);
            if (path == null)
                continue;
            return (
                await File.ReadAllBytesAsync(path, cancellationToken),
                InvoicePdfStorageService.DownloadFileName(contract.WaveInvoiceNumber ?? invoice.WaveInvoiceNumber, contract.WaveInvoicePdfPath));
        }

        var byNumber = pdfs.ResolveFilePath(null, invoice.WaveInvoiceNumber);
        if (byNumber == null)
            return (null, null);

        return (
            await File.ReadAllBytesAsync(byNumber, cancellationToken),
            InvoicePdfStorageService.DownloadFileName(invoice.WaveInvoiceNumber, null));
    }
}

public sealed class InvoiceReminderRenderResult
{
    public bool Found { get; init; }
    public string? Error { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public int? InvoiceId { get; init; }
}
