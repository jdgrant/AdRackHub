namespace AdRackHub.Models;

public enum CustomerStatus
{
    Active,
    Inactive
}

public enum CustomerType
{
    Customer,
    Prospect
}

public enum RouteStatus
{
    Active,
    Inactive
}

public enum RouteProduct
{
    Exits,
    RestArea
}

public enum StopType
{
    Hotel,
    Attraction,
    RestArea,
    Other
}

public enum StopStatus
{
    Active,
    Inactive
}

public enum BillingFrequency
{
    Monthly,
    Quarterly,
    Annual
}

public enum ContactRole
{
    Primary,
    Billing,
    Operations,
    Marketing,
    Other
}

public enum CustomerRouteStatus
{
    Active,
    Inactive
}

public enum CustomerNoteKind
{
    Note,
    Call,
    Email,
    Meeting,
    Action
}

public enum CustomerTaskStatus
{
    Open,
    Completed
}

public enum BillingRunStatus
{
    Draft,
    Submitted,
    PartiallySubmitted,
    Failed
}

public enum BillingRunInvoiceStatus
{
    Pending,
    Submitted,
    Received,
    Canceled,
    Skipped,
    Failed
}

/// <summary>Statuses allowed once an invoice has a Wave invoice number.</summary>
public static class WaveInvoiceStatuses
{
    public static readonly BillingRunInvoiceStatus[] All =
    {
        BillingRunInvoiceStatus.Submitted,
        BillingRunInvoiceStatus.Received,
        BillingRunInvoiceStatus.Canceled
    };

    public static bool IsWaveLifecycle(BillingRunInvoiceStatus status) =>
        status is BillingRunInvoiceStatus.Submitted
            or BillingRunInvoiceStatus.Received
            or BillingRunInvoiceStatus.Canceled;

    public static bool TryParse(string? value, out BillingRunInvoiceStatus status)
    {
        status = BillingRunInvoiceStatus.Submitted;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out BillingRunInvoiceStatus parsed))
        {
            // Accept British spelling from integrations.
            if (string.Equals(value.Trim(), "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                status = BillingRunInvoiceStatus.Canceled;
                return true;
            }

            return false;
        }

        if (!IsWaveLifecycle(parsed))
            return false;

        status = parsed;
        return true;
    }
}
