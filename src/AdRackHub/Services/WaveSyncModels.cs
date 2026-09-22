using System.ComponentModel.DataAnnotations;
using AdRackHub.Models;

namespace AdRackHub.Services;

public class InboundWaveCustomerRequest
{
    [Required]
    public string WaveCustomerId { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
}

public class InboundWaveContractCallbackRequest
{
    [Required]
    public int CustomerContractId { get; set; }

    public string? WaveRecurringInvoiceId { get; set; }
    public string? WaveCustomerId { get; set; }
    public string? Message { get; set; }
}

public class InboundWaveInvoiceCallbackRequest
{
    /// <summary>Local AdRackHub BillingRunInvoice.Id (sent as invoiceId on Create On Send).</summary>
    [Required]
    public int InvoiceId { get; set; }

    public string? WaveInvoiceNumber { get; set; }
    public string? WaveInvoiceId { get; set; }
    public string? WaveInvoiceUrl { get; set; }

    /// <summary>Submitted, Received, or Canceled.</summary>
    public string? Status { get; set; }

    public DateOnly? ReceivedDate { get; set; }

    public string? Message { get; set; }
}

public class WaveSyncWebhookResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public int? CustomerId { get; init; }
    public int? CustomerContractId { get; init; }
    public int? InvoiceId { get; init; }
    public string? WaveCustomerId { get; init; }
    public string? WaveRecurringInvoiceId { get; init; }
    public string? WaveInvoiceNumber { get; init; }
    public bool Created { get; init; }

    public static WaveSyncWebhookResponse Ok(string message, int? customerId = null, string? waveCustomerId = null, bool created = false) =>
        new() { Success = true, Message = message, CustomerId = customerId, WaveCustomerId = waveCustomerId, Created = created };

    public static WaveSyncWebhookResponse ContractOk(string message, int customerContractId, string? waveRecurringInvoiceId = null) =>
        new()
        {
            Success = true,
            Message = message,
            CustomerContractId = customerContractId,
            WaveRecurringInvoiceId = waveRecurringInvoiceId
        };

    public static WaveSyncWebhookResponse InvoiceOk(string message, int invoiceId, string? waveInvoiceNumber = null) =>
        new()
        {
            Success = true,
            Message = message,
            InvoiceId = invoiceId,
            WaveInvoiceNumber = waveInvoiceNumber
        };

    public static WaveSyncWebhookResponse Fail(string message) =>
        new() { Success = false, Message = message };
}

public sealed class WaveOutboundCustomerPayload
{
    public string Event { get; init; } = "customer.created";
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? WaveCustomerId { get; init; }
    public string Status { get; init; } = string.Empty;
    public WaveOutboundContactPayload? PrimaryContact { get; init; }
}

public sealed class WaveOutboundBillingPayload
{
    public string Event { get; init; } = "contract.created";
    public int CustomerContractId { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? WaveCustomerId { get; init; }
    public string ContractName { get; init; } = string.Empty;
    public string Term { get; init; } = string.Empty;
    public int BillingAnchorMonth { get; init; }
    public DateOnly NextBillDate { get; init; }
    public int ServiceMonthMask { get; init; }
    public DateOnly? ContractEndDate { get; init; }
    public decimal TotalAmount { get; init; }
    public IReadOnlyList<WaveOutboundBillingLinePayload> LineItems { get; init; } = Array.Empty<WaveOutboundBillingLinePayload>();
    public WaveRecurringSchedulePayload Schedule { get; init; } = new();
}

public sealed class WaveOutboundBillingLinePayload
{
    public string Description { get; init; } = string.Empty;
    public decimal Quantity { get; init; } = 1;
    public decimal UnitPrice { get; init; }
    public string RouteName { get; init; } = string.Empty;
    public string Product { get; init; } = string.Empty;
    public string? WaveProductId { get; init; }
    public DateOnly? ServiceStartDate { get; init; }
    public DateOnly? ServiceEndDate { get; init; }
    public int SpaceCount { get; init; }
    public string Locations { get; init; } = string.Empty;
    public IReadOnlyList<string> LocationNames { get; init; } = Array.Empty<string>();
    public int PeriodMonthCount { get; init; }
    public decimal PeriodAmount { get; init; }
    public decimal MonthlyRate { get; init; }
    public bool PriceIsPeriodTotal { get; init; } = true;
    public string PriceLabel { get; init; } = string.Empty;
}

public sealed class WaveOutboundContactPayload
{
    public int? ContactId { get; init; }
    public string? Name { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? CellPhone { get; init; }
    public string? Role { get; init; }
    public bool SendInvoice { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? StateCode { get; init; }
    public string? StateName { get; init; }
    /// <summary>Wave Zapier Province / State/Region custom value (e.g. kentucky).</summary>
    public string? Region { get; init; }
    public string? Province { get; init; }
    public string? ProvinceCode { get; init; }
    public string? Country { get; init; }
    public string? CountryCode { get; init; }
    public string? Zip { get; init; }
}

public sealed class WaveRecurringSchedulePayload
{
    public string Frequency { get; init; } = string.Empty;
    public int StartMonth { get; init; }
    public string StartMonthName { get; init; } = string.Empty;
}

/// <summary>
/// Flat payload for Zapier Catch Hook → Wave Find Customer → Wave Create Invoice.
/// Top-level fields map easily in the Zapier editor.
/// </summary>
public sealed class WaveZapierTestInvoicePayload
{
    public string Event { get; init; } = "invoice.due";
    public bool Test { get; init; } = true;
    public int Year { get; init; }
    public int Month { get; init; }
    public string PeriodLabel { get; init; } = string.Empty;
    /// <summary>Local AdRackHub BillingRunInvoice.Id — logged on Create On Send.</summary>
    public int? InvoiceId { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? WaveCustomerId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? ContactName { get; init; }
    public string? BusinessName { get; init; }
    public string? ContactRole { get; init; }
    public string? Phone { get; init; }
    public string? CellPhone { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? StateCode { get; init; }
    public string? StateName { get; init; }
    /// <summary>Wave Zapier Province / State/Region custom value (e.g. kentucky).</summary>
    public string? Region { get; init; }
    public string? Province { get; init; }
    public string? ProvinceCode { get; init; }
    public string? Country { get; init; }
    public string? CountryCode { get; init; }
    public string? Zip { get; init; }
    /// <summary>All contacts with Send Invoice enabled (for CC / multi-recipient delivery).</summary>
    public IReadOnlyList<WaveOutboundContactPayload> InvoiceRecipients { get; init; } = Array.Empty<WaveOutboundContactPayload>();
    public string InvoiceTitle { get; init; } = string.Empty;
    public string? InvoiceMemo { get; init; }
    public string Currency { get; init; } = "USD";
    public DateOnly InvoiceDate { get; init; }
    public DateOnly DueDate { get; init; }
    public DateOnly? ServiceStartDate { get; init; }
    public DateOnly? ServiceEndDate { get; init; }
    public string PriceNote { get; init; } = "Line item amounts are period totals, not monthly rates.";
    public decimal TotalAmount { get; init; }
    public IReadOnlyList<WaveOutboundBillingLinePayload> LineItems { get; init; } = Array.Empty<WaveOutboundBillingLinePayload>();
}

public static class WaveRecurringScheduleMapper
{
    public static WaveRecurringSchedulePayload Map(int months, int anchorMonth) => new()
    {
        Frequency = months switch
        {
            1 => "MONTHLY",
            3 => "QUARTERLY",
            4 => "EVERY_4_MONTHS",
            12 => "YEARLY",
            _ => "MONTHLY"
        },
        StartMonth = anchorMonth,
        StartMonthName = new DateOnly(2000, Math.Clamp(anchorMonth, 1, 12), 1).ToString("MMMM")
    };

    public static WaveRecurringSchedulePayload Map(BillingFrequency term, int anchorMonth) =>
        Map(AnnualBillingHelper.MonthsInTerm(term), anchorMonth);
}
