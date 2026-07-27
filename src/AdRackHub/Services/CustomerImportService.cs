using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class CustomerImportService
{
    private readonly ApplicationDbContext _context;

    public CustomerImportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BillingCustomerImportResult> ImportMissingBillingCustomersAsync(
        Stream csvStream,
        CancellationToken cancellationToken = default)
    {
        var rows = await ParseBillingCsvAsync(csvStream, cancellationToken);
        if (rows.Count == 0)
            throw new InvalidOperationException("No billing customer rows found in CSV.");

        var existingNames = await _context.Customers
            .Select(c => c.CustomerName)
            .ToListAsync(cancellationToken);
        var existingNameSet = new HashSet<string>(
            existingNames.Select(NormalizeName),
            StringComparer.OrdinalIgnoreCase);

        var routeLookup = await _context.Routes
            .ToDictionaryAsync(r => NormalizeName(r.RouteName), r => r, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var added = 0;
        var skipped = 0;
        var configured = 0;

        foreach (var row in rows)
        {
            var normalizedName = NormalizeName(row.CustomerName);
            Customer? customer;

            if (existingNameSet.Contains(normalizedName))
            {
                customer = await _context.Customers
                    .FirstOrDefaultAsync(
                        c => c.CustomerName.ToLower() == normalizedName.ToLower(),
                        cancellationToken);

                if (customer == null)
                {
                    skipped++;
                    continue;
                }

                if (await _context.CustomerContracts.AnyAsync(b => b.CustomerId == customer.Id, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                configured++;
            }
            else
            {
                customer = new Customer
                {
                    CustomerName = row.CustomerName,
                    Status = CustomerStatus.Active
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync(cancellationToken);

                _context.Contacts.Add(new Contact
                {
                    CustomerId = customer.Id,
                    Name = row.CustomerName,
                    Role = ContactRole.Billing
                });
                await _context.SaveChangesAsync(cancellationToken);

                existingNameSet.Add(normalizedName);
                added++;
            }

            if (row.RouteNames.Count == 0)
                continue;

            var routeIds = new List<int>();
            var pricePerRoute = row.RouteNames.Count > 0
                ? Math.Round(row.Price / row.RouteNames.Count, 2, MidpointRounding.AwayFromZero)
                : 0m;

            foreach (var routeName in row.RouteNames)
            {
                if (!routeLookup.TryGetValue(NormalizeName(routeName), out var route))
                {
                    route = new Models.Route
                    {
                        RouteName = routeName,
                        Price = pricePerRoute,
                        BillingFrequency = row.Term,
                        Status = RouteStatus.Active
                    };
                    _context.Routes.Add(route);
                    await _context.SaveChangesAsync(cancellationToken);
                    routeLookup[NormalizeName(routeName)] = route;
                }
                else if (route.Price == 0 && pricePerRoute > 0)
                {
                    route.Price = pricePerRoute;
                    route.BillingFrequency = row.Term;
                }

                routeIds.Add(route.Id);

                var hasAssignment = await _context.CustomerRoutes.AnyAsync(
                    cr => cr.CustomerId == customer.Id && cr.RouteId == route.Id,
                    cancellationToken);
                if (!hasAssignment)
                {
                    _context.CustomerRoutes.Add(new CustomerRoute
                    {
                        CustomerId = customer.Id,
                        RouteId = route.Id,
                        AllStops = true,
                        Status = CustomerRouteStatus.Active
                    });
                }
            }

            var billName = $"{BillingTermDisplay.Label(row.Term)} Bill";
            var billing = new CustomerContract
            {
                CustomerId = customer.Id,
                ContractName = billName,
                Term = row.Term,
                BillingAnchorMonth = row.AnchorMonth
            };
            _context.CustomerContracts.Add(billing);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var routeId in routeIds.Distinct())
            {
                _context.CustomerContractRoutes.Add(new CustomerContractRoute
                {
                    CustomerContractId = billing.Id,
                    RouteId = routeId
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        return new BillingCustomerImportResult
        {
            Added = added,
            Skipped = skipped,
            Configured = configured
        };
    }

    public async Task<CustomerImportResult> ReplaceAllFromCsvAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        var rows = await ParseCsvAsync(csvStream, cancellationToken);
        if (rows.Count == 0)
            throw new InvalidOperationException("No customer rows found in CSV.");

        await ClearAllCustomersAsync(cancellationToken);

        var customers = rows.Select(row => new Customer
        {
            CustomerName = row.CustomerName,
            Status = CustomerStatus.Active
        }).ToList();

        _context.Customers.AddRange(customers);
        await _context.SaveChangesAsync(cancellationToken);

        _context.Contacts.AddRange(customers.Select((customer, index) => new Contact
        {
            CustomerId = customer.Id,
            Name = rows[index].CustomerName,
            Email = rows[index].Email,
            Phone = rows[index].Phone,
            Address = rows[index].Address,
            City = rows[index].City,
            State = rows[index].State,
            Zip = rows[index].Zip,
            Role = ContactRole.Billing
        }));

        await _context.SaveChangesAsync(cancellationToken);

        return new CustomerImportResult
        {
            Imported = rows.Count
        };
    }

    private async Task ClearAllCustomersAsync(CancellationToken cancellationToken)
    {
        _context.BillingRunInvoiceLines.RemoveRange(await _context.BillingRunInvoiceLines.ToListAsync(cancellationToken));
        _context.BillingRunInvoices.RemoveRange(await _context.BillingRunInvoices.ToListAsync(cancellationToken));
        _context.BillingRuns.RemoveRange(await _context.BillingRuns.ToListAsync(cancellationToken));
        _context.CustomerContractRoutes.RemoveRange(await _context.CustomerContractRoutes.ToListAsync(cancellationToken));
        _context.CustomerContracts.RemoveRange(await _context.CustomerContracts.ToListAsync(cancellationToken));
        _context.CustomerRouteStops.RemoveRange(await _context.CustomerRouteStops.ToListAsync(cancellationToken));
        _context.CustomerRoutes.RemoveRange(await _context.CustomerRoutes.ToListAsync(cancellationToken));
        _context.Contacts.RemoveRange(await _context.Contacts.ToListAsync(cancellationToken));
        _context.Customers.RemoveRange(await _context.Customers.ToListAsync(cancellationToken));
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<List<ParsedBillingCustomerRow>> ParseBillingCsvAsync(
        Stream csvStream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(csvStream);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidOperationException("CSV file is empty.");

        var headers = ParseCsvLine(headerLine);
        var columnMap = MapBillingColumns(headers);
        if (!columnMap.ContainsKey("CustomerName"))
            throw new InvalidOperationException("CSV must include a Customer column.");

        var rows = new List<ParsedBillingCustomerRow>();
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = ParseCsvLine(line);
            var customerName = GetValue(values, columnMap, "CustomerName");
            if (string.IsNullOrWhiteSpace(customerName))
                continue;

            var termText = GetValue(values, columnMap, "Term") ?? "Q";
            var term = ParseTerm(termText);
            var priceText = GetValue(values, columnMap, "Price") ?? "0";
            if (!decimal.TryParse(priceText, out var price))
                price = 0;

            var anchorText = GetValue(values, columnMap, "Anchor") ?? "1";
            if (!int.TryParse(anchorText, out var anchor) || anchor is < 1 or > 12)
                anchor = 1;

            var routesText = GetValue(values, columnMap, "Routes");
            var routeNames = ParseRouteNames(routesText);

            rows.Add(new ParsedBillingCustomerRow
            {
                CustomerName = customerName.Trim(),
                Term = term,
                Price = price,
                AnchorMonth = anchor,
                RouteNames = routeNames
            });
        }

        return rows;
    }

    private static Dictionary<string, int> MapBillingColumns(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var key = headers[i].Trim().ToLowerInvariant() switch
            {
                "customer" or "customer name" or "name" or "business name" => "CustomerName",
                "term" => "Term",
                "price" or "total" or "total monthly" => "Price",
                "anchor" or "billing start month" or "billing anchor month" => "Anchor",
                "routes" or "route" => "Routes",
                _ => null
            };

            if (key != null && !map.ContainsKey(key))
                map[key] = i;
        }

        return map;
    }

    private static BillingFrequency ParseTerm(string term) => term.Trim().ToUpperInvariant() switch
    {
        "M" or "MONTHLY" => BillingFrequency.Monthly,
        "Y" or "YEARLY" or "ANNUAL" => BillingFrequency.Annual,
        _ => BillingFrequency.Quarterly
    };

    private static List<string> ParseRouteNames(string? routesText)
    {
        if (string.IsNullOrWhiteSpace(routesText))
            return [];

        return routesText
            .Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeName(string name) => name.Trim();

    private static async Task<List<ParsedCustomerRow>> ParseCsvAsync(Stream csvStream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(csvStream);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidOperationException("CSV file is empty.");

        var headers = ParseCsvLine(headerLine);
        var columnMap = MapColumns(headers);
        if (!columnMap.ContainsKey("CustomerName"))
            throw new InvalidOperationException("CSV must include a Customer Name column.");

        var rows = new List<ParsedCustomerRow>();
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = ParseCsvLine(line);
            var customerName = GetValue(values, columnMap, "CustomerName");
            if (string.IsNullOrWhiteSpace(customerName))
                continue;

            rows.Add(new ParsedCustomerRow
            {
                CustomerName = customerName.Trim(),
                Email = NullIfEmpty(GetValue(values, columnMap, "Email")),
                Address = NullIfEmpty(GetValue(values, columnMap, "Address")),
                City = NullIfEmpty(GetValue(values, columnMap, "City")),
                State = NullIfEmpty(GetValue(values, columnMap, "State")),
                Zip = NullIfEmpty(GetValue(values, columnMap, "Zip")),
                Phone = NullIfEmpty(GetValue(values, columnMap, "Phone"))
            });
        }

        return rows;
    }

    private static Dictionary<string, int> MapColumns(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var key = headers[i].Trim().ToLowerInvariant() switch
            {
                "customer name" or "name" or "business name" => "CustomerName",
                "email" => "Email",
                "address line 1" or "address" => "Address",
                "city" => "City",
                "province/state" or "state" => "State",
                "postal/zip" or "postal/zip code" or "zip" or "zip code" => "Zip",
                "phone" => "Phone",
                _ => null
            };

            if (key != null && !map.ContainsKey(key))
                map[key] = i;
        }

        return map;
    }

    private static string? GetValue(IReadOnlyList<string> values, Dictionary<string, int> columnMap, string key)
    {
        if (!columnMap.TryGetValue(key, out var index) || index >= values.Count)
            return null;
        return values[index].Trim();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = "";
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(current);
                current = "";
                continue;
            }

            current += ch;
        }

        values.Add(current);
        return values;
    }

    private sealed class ParsedCustomerRow
    {
        public string CustomerName { get; init; } = string.Empty;
        public string? Email { get; init; }
        public string? Address { get; init; }
        public string? City { get; init; }
        public string? State { get; init; }
        public string? Zip { get; init; }
        public string? Phone { get; init; }
    }

    private sealed class ParsedBillingCustomerRow
    {
        public string CustomerName { get; init; } = string.Empty;
        public BillingFrequency Term { get; init; }
        public decimal Price { get; init; }
        public int AnchorMonth { get; init; }
        public List<string> RouteNames { get; init; } = [];
    }
}

public class CustomerImportResult
{
    public int Imported { get; set; }
}

public class BillingCustomerImportResult
{
    public int Added { get; set; }
    public int Skipped { get; set; }
    public int Configured { get; set; }
}
