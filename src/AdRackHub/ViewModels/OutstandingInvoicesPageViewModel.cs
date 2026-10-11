using AdRackHub.Models;

namespace AdRackHub.ViewModels;

public class OutstandingInvoicesPageViewModel
{
    public const string AgeUnder30 = "under30";
    public const string Age30To60 = "30-60";
    public const string AgeOver60 = "over60";

    public string? Search { get; set; }
    public int? MinWeeksLate { get; set; }
    public string? Age { get; set; }
    public DateOnly AsOf { get; set; }
    public bool WaveConnected { get; set; }
    public bool WaveSessionExpired { get; set; }
    public string WaveActionBlockedReason =>
        WaveSessionExpired
            ? "Wave session expired. Reset Wave, then try again."
            : "Connect to Wave to send or receive invoices.";
    public List<OutstandingInvoiceRow> Invoices { get; set; } = new();
    public int AllInvoiceCount { get; set; }
    public int AllUniqueClientCount { get; set; }
    public decimal AllOutstandingAmount { get; set; }
    public OutstandingAgingBucket Under30 { get; set; } = new();
    public OutstandingAgingBucket From30To60 { get; set; } = new();
    public OutstandingAgingBucket Over60 { get; set; } = new();

    public decimal OutstandingAmount => Invoices.Sum(i => i.Amount);
    public int InvoiceCount => Invoices.Count;
    public int UniqueClientCount => Invoices.Select(i => i.CustomerId).Distinct().Count();

    public static IReadOnlyList<(int? Weeks, string Label)> WeekFilters { get; } =
    [
        (null, "All outstanding"),
        (1, "1+ weeks"),
        (2, "2+ weeks"),
        (4, "4+ weeks"),
        (8, "8+ weeks"),
        (12, "12+ weeks")
    ];

    public static OutstandingAgingBucket Bucket(IEnumerable<OutstandingInvoiceRow> rows, Func<int, bool> match) =>
        From(rows.Where(r => match(r.DaysLate)));

    public static OutstandingAgingBucket From(IEnumerable<OutstandingInvoiceRow> rows)
    {
        var list = rows as IList<OutstandingInvoiceRow> ?? rows.ToList();
        return new OutstandingAgingBucket
        {
            Amount = list.Sum(r => r.Amount),
            InvoiceCount = list.Count
        };
    }
}

public class OutstandingAgingBucket
{
    public decimal Amount { get; init; }
    public int InvoiceCount { get; init; }
    public string Summary => $"{Amount:C0} / {InvoiceCount}";
}

public class OutstandingInvoiceRow
{
    public int InvoiceId { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public InvoiceReceiptMethod InvoiceReceiptMethod { get; init; }
    public string? WaveInvoiceNumber { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public string PeriodLabel { get; init; } = string.Empty;
    public DateOnly InvoiceDate { get; init; }
    public int DaysLate { get; init; }
    public int WeeksLate { get; init; }
    public decimal Amount { get; init; }
    public string ContractNames { get; init; } = string.Empty;
    public BillingRunInvoiceStatus Status { get; init; }
    public bool HasPdf { get; init; }

    public string LateLabel =>
        DaysLate <= 0 ? "Current"
        : WeeksLate == 0 ? $"{DaysLate} day{(DaysLate == 1 ? "" : "s")}"
        : $"{WeeksLate} week{(WeeksLate == 1 ? "" : "s")}";
}
