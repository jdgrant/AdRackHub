using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public class CustomerBrochureInventory
{
    public int Id { get; set; }

    [Required]
    public int CustomerId { get; set; }

    [Required]
    [Display(Name = "Count")]
    [Range(0, int.MaxValue, ErrorMessage = "Count must be zero or greater.")]
    public int Quantity { get; set; }

    [Required]
    [Display(Name = "Inventory Date")]
    [DataType(DataType.Date)]
    public DateOnly InventoryDate { get; set; }

    [Display(Name = "Warehouse")]
    public BrochureWarehouse? Warehouse { get; set; }

    [StringLength(50)]
    [Display(Name = "Rack")]
    public string? Rack { get; set; }

    [StringLength(50)]
    [Display(Name = "Bin")]
    public string? Bin { get; set; }

    [Display(Name = "Top/Bottom")]
    public BrochureShelf? Shelf { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    [Display(Name = "Logged by")]
    public string? CreatedBy { get; set; }

    public Customer Customer { get; set; } = null!;

    public string? LocationLabel => WarehouseLocation.Format(Warehouse, Rack, Bin, Shelf);
}

public static class WarehouseLocation
{
    public static string? Format(string? rack, string? bin) =>
        Format(null, rack, bin, null);

    public static string? Format(BrochureWarehouse? warehouse, string? rack, string? bin, BrochureShelf? shelf = null)
    {
        rack = NullIfEmpty(rack);
        bin = NullIfEmpty(bin);
        var parts = new List<string>();
        if (Code(warehouse) is { } code)
            parts.Add(code);
        if (rack != null && bin != null)
            parts.Add($"Rack {rack} · Bin {bin}");
        else if (rack != null)
            parts.Add($"Rack {rack}");
        else if (bin != null)
            parts.Add($"Bin {bin}");
        if (ShelfCode(shelf) is { } shelfCode)
            parts.Add(shelfCode);
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    public static string? FormatCompact(string? rack, string? bin) =>
        FormatCompact(null, rack, bin, null);

    public static string? FormatCompact(BrochureWarehouse? warehouse, string? rack, string? bin, BrochureShelf? shelf = null)
    {
        rack = NullIfEmpty(rack);
        bin = NullIfEmpty(bin);
        var parts = new List<string>();
        if (Code(warehouse) is { } code)
            parts.Add(code);
        var location = rack != null && bin != null ? $"{rack} / {bin}" : rack ?? bin;
        if (location != null)
            parts.Add(location);
        if (ShelfCode(shelf) is { } shelfCode)
            parts.Add(shelfCode);
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    public static string? FormatLabelLine(BrochureWarehouse? warehouse, string? rack, string? bin, BrochureShelf? shelf = null)
    {
        rack = NullIfEmpty(rack);
        bin = NullIfEmpty(bin);
        if (rack == null && bin == null)
            return null;
        var left = $"{Code(warehouse)}{rack}";
        var line = bin == null
            ? left
            : string.IsNullOrEmpty(left) ? bin : $"{left} / {bin}";
        if (ShelfCode(shelf) is { } shelfCode)
            line = string.IsNullOrEmpty(line) ? shelfCode : $"{line} {shelfCode}";
        return NullIfEmpty(line);
    }

    public static string? FormatBracket(string? rack, string? bin) =>
        FormatBracket(null, rack, bin, null);

    public static string? FormatBracket(BrochureWarehouse? warehouse, string? rack, string? bin, BrochureShelf? shelf = null, string? brochureCode = null)
    {
        rack = NullIfEmpty(rack);
        bin = NullIfEmpty(bin);
        var location = rack != null && bin != null ? $"{rack}/{bin}" : rack ?? bin;
        var id = NullIfEmpty(brochureCode);
        if (location == null && id == null)
            return null;
        var parts = new List<string>();
        if (id != null)
            parts.Add(id);
        if (Code(warehouse) is { } code)
            parts.Add(code);
        if (location != null)
            parts.Add(location);
        if (ShelfCode(shelf) is { } shelfCode)
            parts.Add(shelfCode);
        return $"[{string.Join(" ", parts)}]";
    }

    public static BrochureWarehouse? Parse(string? value)
    {
        var text = NullIfEmpty(value);
        if (text == null)
            return null;
        if (text.Equals("K", StringComparison.OrdinalIgnoreCase)
            || text.Equals("KY", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Kentucky", StringComparison.OrdinalIgnoreCase))
            return BrochureWarehouse.K;
        if (text.Equals("O", StringComparison.OrdinalIgnoreCase)
            || text.Equals("OH", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Ohio", StringComparison.OrdinalIgnoreCase))
            return BrochureWarehouse.O;
        return Enum.TryParse<BrochureWarehouse>(text, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }

    public static BrochureWarehouse? DefaultKy(BrochureWarehouse? warehouse, string? rack, string? bin)
    {
        if (warehouse != null)
            return warehouse;
        return NullIfEmpty(rack) != null || NullIfEmpty(bin) != null
            ? BrochureWarehouse.K
            : null;
    }

    public static BrochureShelf? ParseShelf(string? value)
    {
        var text = NullIfEmpty(value);
        if (text == null)
            return null;
        if (text.Equals("T", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Top", StringComparison.OrdinalIgnoreCase))
            return BrochureShelf.Top;
        if (text.Equals("B", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Bottom", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Bot", StringComparison.OrdinalIgnoreCase))
            return BrochureShelf.Bottom;
        return Enum.TryParse<BrochureShelf>(text, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }

    public static string? Code(BrochureWarehouse? warehouse) =>
        warehouse?.ToString();

    public static string? ShelfCode(BrochureShelf? shelf) =>
        shelf?.ToString();

    public static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? FormatBracketMany(
        IEnumerable<CustomerWarehouseLocation>? locations,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        string? brochureCode)
    {
        var slots = locations?
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .Select(l => FormatCompact(l.Warehouse, l.Rack, l.Bin, l.Shelf))
            .Where(s => s != null)
            .Cast<string>()
            .ToList() ?? new List<string>();
        if (slots.Count == 0)
            return FormatBracket(warehouse, rack, bin, shelf, brochureCode);

        var id = NullIfEmpty(brochureCode);
        return id == null ? $"[{string.Join("; ", slots)}]" : $"[{id} {string.Join("; ", slots)}]";
    }

    public static bool Matches(
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        BrochureWarehouse? otherWarehouse,
        string? otherRack,
        string? otherBin,
        BrochureShelf? otherShelf) =>
        warehouse == otherWarehouse
        && string.Equals(NullIfEmpty(rack), NullIfEmpty(otherRack), StringComparison.OrdinalIgnoreCase)
        && string.Equals(NullIfEmpty(bin), NullIfEmpty(otherBin), StringComparison.OrdinalIgnoreCase)
        && shelf == otherShelf;

    public static CustomerWarehouseLocation? Upsert(
        Customer customer,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf)
    {
        rack = NullIfEmpty(rack);
        bin = NullIfEmpty(bin);
        warehouse = DefaultKy(warehouse, rack, bin);
        if (warehouse == null && rack == null && bin == null && shelf == null)
            return null;

        var match = customer.WarehouseLocations.FirstOrDefault(l =>
            Matches(l.Warehouse, l.Rack, l.Bin, l.Shelf, warehouse, rack, bin, shelf));
        if (match != null)
        {
            match.Warehouse = warehouse;
            match.Rack = rack;
            match.Bin = bin;
            match.Shelf = shelf;
            SyncPrimary(customer);
            return match;
        }

        var location = new CustomerWarehouseLocation
        {
            CustomerId = customer.Id,
            Warehouse = warehouse,
            Rack = rack,
            Bin = bin,
            Shelf = shelf,
            SortOrder = customer.WarehouseLocations.Count == 0
                ? 0
                : customer.WarehouseLocations.Max(l => l.SortOrder) + 1
        };
        customer.WarehouseLocations.Add(location);
        SyncPrimary(customer);
        return location;
    }

    public static void SyncPrimary(Customer customer)
    {
        var first = customer.WarehouseLocations
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .FirstOrDefault();
        customer.Warehouse = first?.Warehouse;
        customer.WarehouseRack = first?.Rack;
        customer.WarehouseBin = first?.Bin;
        customer.WarehouseShelf = first?.Shelf;
    }
}
