using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace AdRackHub.Models;

public enum CustomerStatus
{
    Active,
    Inactive
}

public enum CustomerType
{
    Customer,
    Prospect,
    [Display(Name = "Expanded Prospect")]
    ExpandedProspect
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
    Other,
    ProspectStop
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
    EveryFourMonths,
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
    Action,
    Task,
    [Display(Name = "Brochures Needed (911)")]
    BrochuresNeeded = 911
}

public enum CustomerNoteStatus
{
    New,
    Working,
    Done
}

public static class EnumDisplay
{
    public static string Name(this Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
    }

    public static string KindCss(this CustomerNoteKind kind) => kind switch
    {
        CustomerNoteKind.Call => "call",
        CustomerNoteKind.Email => "email",
        CustomerNoteKind.Meeting => "meeting",
        CustomerNoteKind.Action => "action",
        CustomerNoteKind.Task => "task",
        CustomerNoteKind.BrochuresNeeded => "brochures",
        _ => "note"
    };

    public static IEnumerable<CustomerNoteKind> ActivityKinds() =>
        Enum.GetValues<CustomerNoteKind>();
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

public enum InvoiceReceiptMethod
{
    Mail,
    Email,
    Both
}

public static class InvoiceReceiptMethods
{
    public static bool IncludesEmail(this InvoiceReceiptMethod method) =>
        method is InvoiceReceiptMethod.Email or InvoiceReceiptMethod.Both;

    public static bool IncludesMail(this InvoiceReceiptMethod method) =>
        method is InvoiceReceiptMethod.Mail or InvoiceReceiptMethod.Both;
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
