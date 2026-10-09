namespace AdRackHub.ViewModels;

public class WarehouseInvoicePageViewModel
{
    public DateOnly InventoryDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? CountedBy { get; set; }
    public string ActiveWarehouse { get; set; } = "K";
    public List<WarehouseInvoiceTabViewModel> Tabs { get; set; } = new();
    public List<WarehouseInvoiceRowForm> Rows { get; set; } = new();
}

public class WarehouseInvoiceTabViewModel
{
    public string Code { get; set; } = "K";
    public string Title { get; set; } = string.Empty;
    public List<int> RowIndexes { get; set; } = new();
}

public class WarehouseInvoiceRowForm
{
    public int CustomerId { get; set; }
    public string BrochureName { get; set; } = string.Empty;
    public string? BrochureCode { get; set; }
    public string? Location { get; set; }
    public string? ContractLabel { get; set; }
    public string? Warehouse { get; set; }
    public string? Rack { get; set; }
    public string? Bin { get; set; }
    public string? Shelf { get; set; }
    public int? LastQuantity { get; set; }
    public int? LastReceivedQuantity { get; set; }
    public DateOnly? LastCountedDate { get; set; }
    public int? Cases { get; set; }
    public int? PerCase { get; set; }
    public int? Total { get; set; }
}
