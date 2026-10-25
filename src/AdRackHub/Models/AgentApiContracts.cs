namespace AdRackHub.Models;

public class AgentCustomerListDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public bool HasMore { get; set; }
    public List<AgentCustomerDto> Items { get; set; } = new();
}

public class AgentCustomerDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsHighValueProspect { get; set; }
    public bool IsAtRisk { get; set; }
    public bool NeedsMoreInfo { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? WebUrl { get; set; }
    public string InvoiceReceiptMethod { get; set; } = "Mail";
    public string? AccountManager { get; set; }
    public string? HubUrl { get; set; }
    public List<AgentContactDto> Contacts { get; set; } = new();
}

public class AgentContactDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Title { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? CellPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? WebUrl { get; set; }
    public string? LinkedInUrl { get; set; }
    public string Role { get; set; } = "Primary";
    public bool SendInvoice { get; set; }
}

public class AgentErrorDto
{
    public AgentErrorDto()
    {
    }

    public AgentErrorDto(string error, IReadOnlyList<string>? errors = null)
    {
        Error = error;
        if (errors is { Count: > 0 })
            Errors = errors.ToList();
    }

    public string Error { get; set; } = string.Empty;
    public List<string>? Errors { get; set; }
}

public sealed class AgentCallResult<T>
{
    public int StatusCode { get; init; }
    public T? Body { get; init; }
    public AgentErrorDto? Error { get; init; }
    public string? Location { get; init; }

    public static AgentCallResult<T> Ok(T body) => new() { StatusCode = 200, Body = body };

    public static AgentCallResult<T> Created(T body, string location) =>
        new() { StatusCode = 201, Body = body, Location = location };

    public static AgentCallResult<T> Fail(int status, string error, List<string>? errors = null) =>
        new() { StatusCode = status, Error = new AgentErrorDto(error, errors) };

    public static AgentCallResult<T> Invalid(List<string> errors) =>
        Fail(400, errors.Count == 1 ? errors[0] : "The request is invalid.", errors);

    public static AgentCallResult<T> Missing(string message) => Fail(404, message);
}

public class AgentActivityListDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public bool HasMore { get; set; }
    public List<AgentActivityDto> Items { get; set; } = new();
}

public class AgentActivityDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerType { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? DueDate { get; set; }
    public string Body { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public bool IsOverdue { get; set; }
    public string HubUrl { get; set; } = string.Empty;
}

public class AgentStopListDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public bool HasMore { get; set; }
    public List<AgentStopDto> Items { get; set; } = new();
}

public class AgentStopDto
{
    public int Id { get; set; }
    public string StopName { get; set; } = string.Empty;
    public int RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? State { get; set; }
    public string? HighwayExit { get; set; }
    public string? LastVisitedAt { get; set; }
    public string HubUrl { get; set; } = string.Empty;
}

public class AgentVisitListDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public bool HasMore { get; set; }
    public List<AgentVisitDto> Items { get; set; } = new();
}

public class AgentVisitDto
{
    public int Id { get; set; }
    public int StopId { get; set; }
    public string StopName { get; set; } = string.Empty;
    public int RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public string VisitedAt { get; set; } = string.Empty;
    public string? DriverName { get; set; }
    public string? VisitedByName { get; set; }
    public string? Notes { get; set; }
    public string HubUrl { get; set; } = string.Empty;
    public List<AgentVisitPhotoDto> Photos { get; set; } = new();
}

public class AgentVisitPhotoDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string HubUrl { get; set; } = string.Empty;
}

public class AgentVisitUpload
{
    public int? StopId { get; set; }
    public string? RouteName { get; set; }
    public string? StopName { get; set; }
    public string? DriverName { get; set; }
    public string? VisitedAt { get; set; }
    public string? Notes { get; set; }
    public string? BeforeImageBase64 { get; set; }
    public string? BeforeFileName { get; set; }
    public string? BeforeContentType { get; set; }
    public string? AfterImageBase64 { get; set; }
    public string? AfterFileName { get; set; }
    public string? AfterContentType { get; set; }
}

public class AgentBillingDueDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public decimal DueTotal { get; set; }
    public int DueCustomerCount { get; set; }
    public int DueContractCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public bool HasMore { get; set; }
    public string HubUrl { get; set; } = string.Empty;
    public List<AgentBillingDueItemDto> Items { get; set; } = new();
    public List<AgentBillingInvoiceDto> SubmittedInvoices { get; set; } = new();
}

public class AgentBillingDueItemDto
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int ContractId { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public string Term { get; set; } = string.Empty;
    public string NextBillDate { get; set; } = string.Empty;
    public List<string> Routes { get; set; } = new();
    public decimal Amount { get; set; }
    public string HubUrl { get; set; } = string.Empty;
}

public class AgentBillingInvoiceDto
{
    public int InvoiceId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public string? WaveInvoiceNumber { get; set; }
    public decimal Amount { get; set; }
    public string? ContractNames { get; set; }
    public string HubUrl { get; set; } = string.Empty;
}

public class AgentBillingOutstandingDto
{
    public string AsOf { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int InvoiceCount { get; set; }
    public int ClientCount { get; set; }
    public AgentBillingAgingDto Under30 { get; set; } = new();
    public AgentBillingAgingDto From30To60 { get; set; } = new();
    public AgentBillingAgingDto Over60 { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public bool HasMore { get; set; }
    public string HubUrl { get; set; } = string.Empty;
    public List<AgentBillingOutstandingItemDto> Items { get; set; } = new();
}

public class AgentBillingAgingDto
{
    public decimal Amount { get; set; }
    public int InvoiceCount { get; set; }
}

public class AgentBillingOutstandingItemDto
{
    public int InvoiceId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public string InvoiceDate { get; set; } = string.Empty;
    public int DaysLate { get; set; }
    public int WeeksLate { get; set; }
    public string? WaveInvoiceNumber { get; set; }
    public string ContractNames { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string HubUrl { get; set; } = string.Empty;
}

public class AgentBillingForecastDto
{
    public decimal ForecastTotal { get; set; }
    public int ForecastInvoiceCount { get; set; }
    public int SelectedYear { get; set; }
    public int SelectedMonth { get; set; }
    public string SelectedPeriodLabel { get; set; } = string.Empty;
    public bool IgnoreEndDate { get; set; }
    public string HubUrl { get; set; } = string.Empty;
    public List<AgentBillingForecastMonthDto> Months { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public bool HasMore { get; set; }
    public List<AgentBillingForecastLineDto> Lines { get; set; } = new();
}

public class AgentBillingForecastMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public int CustomerCount { get; set; }
    public decimal Amount { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsSelected { get; set; }
}

public class AgentBillingForecastLineDto
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int ContractId { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public string Term { get; set; } = string.Empty;
    public string? ContractEndDate { get; set; }
    public decimal Amount { get; set; }
    public bool Sent { get; set; }
    public string HubUrl { get; set; } = string.Empty;
}
