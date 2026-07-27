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
    Skipped,
    Failed
}
