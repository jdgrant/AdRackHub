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

public class WaveSyncWebhookResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public int? CustomerId { get; init; }
    public int? CustomerContractId { get; init; }
    public string? WaveCustomerId { get; init; }
    public string? WaveRecurringInvoiceId { get; init; }
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
}

public sealed class WaveOutboundContactPayload
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
}

public sealed class WaveRecurringSchedulePayload
{
    public string Frequency { get; init; } = string.Empty;
    public int StartMonth { get; init; }
    public string StartMonthName { get; init; } = string.Empty;
}

public static class WaveRecurringScheduleMapper
{
    public static WaveRecurringSchedulePayload Map(BillingFrequency term, int anchorMonth) => new()
    {
        Frequency = term switch
        {
            BillingFrequency.Monthly => "MONTHLY",
            BillingFrequency.Quarterly => "QUARTERLY",
            BillingFrequency.Annual => "YEARLY",
            _ => term.ToString().ToUpperInvariant()
        },
        StartMonth = anchorMonth,
        StartMonthName = new DateOnly(2000, Math.Clamp(anchorMonth, 1, 12), 1).ToString("MMMM")
    };
}
