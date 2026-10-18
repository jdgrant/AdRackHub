namespace AdRackHub.ViewModels;

public class ContractEndDatesPageViewModel
{
    public DateOnly AsOf { get; set; }
    public List<ContractEndDateRow> Rows { get; set; } = new();

    public int ContractCount => Rows.Count;
    public int ClientCount => Rows.Select(r => r.CustomerId).Distinct().Count();
    public int ExpiredCount => Rows.Count(r => r.IsExpired);
    public int EndingSoonCount => Rows.Count(r => r.IsEndingSoon);
    public int LaterCount => Rows.Count(r => !r.IsExpired && !r.IsEndingSoon);
}

public class ContractEndDateRow
{
    public const int EndingSoonDays = 30;

    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public int ContractId { get; init; }
    public string ContractName { get; init; } = string.Empty;
    public string TermLabel { get; init; } = string.Empty;
    public DateOnly EndDate { get; init; }
    public int DaysUntilEnd { get; init; }

    public bool IsExpired => DaysUntilEnd < 0;
    public bool IsEndingSoon => DaysUntilEnd is >= 0 and <= EndingSoonDays;

    public string StatusLabel =>
        DaysUntilEnd < 0
            ? $"Expired {Math.Abs(DaysUntilEnd)} day{(Math.Abs(DaysUntilEnd) == 1 ? "" : "s")} ago"
            : DaysUntilEnd == 0
                ? "Ends today"
                : $"Ends in {DaysUntilEnd} day{(DaysUntilEnd == 1 ? "" : "s")}";
}
