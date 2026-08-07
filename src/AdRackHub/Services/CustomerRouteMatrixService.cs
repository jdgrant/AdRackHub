using ClosedXML.Excel;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class CustomerRouteMatrixService
{
    public const string WorksheetName = "Customer Routes";
    public const string CustomerColumn = "Customer";
    public const string BillingColumnSuffix = " Billing";
    public const string LegacyBillingCycleColumn = "Billing Cycle";
    public const string TemplateFileName = "customer-route-matrix.xlsx";
    public const string SampleFileName = "customer-route-matrix-sample.xlsx";

    private readonly ApplicationDbContext _context;

    public CustomerRouteMatrixService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task ExportTemplateAsync(Stream outputStream, string? contentRootPath = null, CancellationToken cancellationToken = default)
    {
        await ExportMatrixAsync(outputStream, contentRootPath, includeSampleRows: false, cancellationToken);
    }

    public async Task ExportSampleAsync(Stream outputStream, string? contentRootPath = null, CancellationToken cancellationToken = default)
    {
        await ExportMatrixAsync(outputStream, contentRootPath, includeSampleRows: false, cancellationToken);
    }

    private async Task ExportMatrixAsync(
        Stream outputStream,
        string? contentRootPath,
        bool includeSampleRows,
        CancellationToken cancellationToken)
    {
        var routes = await GetExportRoutesAsync(cancellationToken);
        var customerNames = await GetAllCustomerNamesAsync(contentRootPath, cancellationToken);
        var customerData = await LoadCustomerExportDataAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(WorksheetName);
        WriteInstructionsSheet(workbook);

        var col = 1;
        worksheet.Cell(1, col++).Value = CustomerColumn;
        foreach (var route in routes)
        {
            worksheet.Cell(1, col++).Value = route.RouteName;
            worksheet.Cell(1, col++).Value = GetRouteBillingColumnName(route.RouteName);
        }

        var headerRow = worksheet.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

        var row = 2;

        if (includeSampleRows)
        {
            var sampleRows = new[]
            {
                new SampleMatrixRow("Example Visitor Center", BillingFrequency.Quarterly, [1200m, 0m, 800m, 600m]),
                new SampleMatrixRow("Example Museum", BillingFrequency.Annual, [2400m, 1800m, 0m, 0m, 1200m]),
                new SampleMatrixRow("Example Hotel", BillingFrequency.Monthly, [720m, 720m, 0m, 0m, 0m, 480m])
            };

            foreach (var sample in sampleRows)
            {
                WriteMatrixRow(worksheet, row, sample.CustomerName, routes, routeIndex =>
                {
                    var price = routeIndex < sample.AnnualPrices.Length ? sample.AnnualPrices[routeIndex] : 0m;
                    var term = price > 0 ? sample.Term : (BillingFrequency?)null;
                    return (price > 0 ? price : null, term);
                }, italic: true);
                row++;
            }
        }

        foreach (var customerName in customerNames)
        {
            customerData.TryGetValue(NormalizeName(customerName), out var data);

            WriteMatrixRow(worksheet, row, customerName, routes, routeIndex =>
            {
                var route = routes[routeIndex];
                return (data?.GetAnnualPrice(route.Id), data?.GetBillingTerm(route.Id));
            });
            row++;
        }

        worksheet.Columns().AdjustToContents();
        worksheet.SheetView.FreezeRows(1);
        worksheet.SheetView.FreezeColumns(1);
        workbook.SaveAs(outputStream);
    }

    private async Task<List<Models.Route>> GetExportRoutesAsync(CancellationToken cancellationToken)
    {
        var routes = await _context.Routes
            .Where(r => r.Status == RouteStatus.Active)
            .ToListAsync(cancellationToken);

        var routeByName = routes.ToDictionary(r => NormalizeName(r.RouteName), r => r, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<Models.Route>();

        foreach (var routeName in DbInitializer.StandardRoutes)
        {
            if (routeByName.TryGetValue(NormalizeName(routeName), out var route))
                ordered.Add(route);
            else
                ordered.Add(new Models.Route { RouteName = routeName, Status = RouteStatus.Active });
        }

        return ordered;
    }

    public static string GetRouteBillingColumnName(string routeName) => $"{routeName}{BillingColumnSuffix}";

    public static bool IsStandardRoute(string routeName) =>
        DbInitializer.StandardRoutes.Contains(routeName, StringComparer.OrdinalIgnoreCase);

    private async Task<List<string>> GetAllCustomerNamesAsync(string? contentRootPath, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dbNames = await _context.Customers
            .OrderBy(c => c.CustomerName)
            .Select(c => c.CustomerName)
            .ToListAsync(cancellationToken);

        foreach (var name in dbNames)
            names.Add(name.Trim());

        foreach (var name in ReadCustomerNamesFromCsv(contentRootPath))
            names.Add(name);

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> ReadCustomerNamesFromCsv(string? contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath))
            yield break;

        var csvPath = Path.Combine(contentRootPath, "Data", "Imports", "customers.csv");
        if (!File.Exists(csvPath))
            yield break;

        var isHeader = true;
        foreach (var line in File.ReadLines(csvPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var name = line.Trim().Trim('"');
            if (isHeader)
            {
                isHeader = false;
                if (name.Equals("Customer Name", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Customer", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            if (!string.IsNullOrWhiteSpace(name))
                yield return name;
        }
    }

    private async Task<Dictionary<string, CustomerMatrixExportData>> LoadCustomerExportDataAsync(CancellationToken cancellationToken)
    {
        var customers = await _context.Customers
            .Include(c => c.CustomerRoutes)
            .Include(c => c.Contracts)
                .ThenInclude(b => b.ContractRoutes)
            .ToListAsync(cancellationToken);

        return customers.ToDictionary(
            c => NormalizeName(c.CustomerName),
            c => new CustomerMatrixExportData(c),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteMatrixRow(
        IXLWorksheet worksheet,
        int row,
        string customerName,
        IReadOnlyList<Models.Route> routes,
        Func<int, (decimal? AnnualPrice, BillingFrequency? Term)> getRouteData,
        bool italic = false)
    {
        var col = 1;
        worksheet.Cell(row, col).Value = customerName;
        if (italic)
        {
            worksheet.Cell(row, col).Style.Font.Italic = true;
            worksheet.Cell(row, col).Style.Font.FontColor = XLColor.Gray;
        }
        col++;

        for (var routeIndex = 0; routeIndex < routes.Count; routeIndex++)
        {
            var (annualPrice, term) = getRouteData(routeIndex);
            if (annualPrice is > 0)
            {
                worksheet.Cell(row, col).Value = annualPrice.Value;
                worksheet.Cell(row, col).Style.NumberFormat.Format = "$#,##0.00";
            }
            col++;

            if (term.HasValue)
                worksheet.Cell(row, col).Value = AnnualBillingHelper.FormatBillingCycle(term.Value);
            col++;
        }
    }

    public async Task<CustomerRouteMatrixImportResult> ImportAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook(inputStream);
        var worksheet = workbook.Worksheets.FirstOrDefault(w =>
            w.Name.Equals(WorksheetName, StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.First();

        var header = ReadHeaderRow(worksheet);
        if (header.CustomerColumn <= 0)
            throw new InvalidOperationException($"Missing required '{CustomerColumn}' column.");
        if (header.RouteColumns.Count == 0)
            throw new InvalidOperationException("No route columns found in the header row.");

        var routeLookup = await _context.Routes
            .Where(r => DbInitializer.StandardRoutes.Contains(r.RouteName))
            .ToDictionaryAsync(r => NormalizeName(r.RouteName), r => r, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var result = new CustomerRouteMatrixImportResult();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var customerName = worksheet.Cell(rowNumber, header.CustomerColumn).GetString().Trim();
            if (string.IsNullOrWhiteSpace(customerName))
                continue;

            if (customerName.StartsWith("Example ", StringComparison.OrdinalIgnoreCase) ||
                customerName.StartsWith("(Add your customers", StringComparison.OrdinalIgnoreCase))
            {
                result.SkippedRows++;
                continue;
            }

            var legacyBillingCycleText = header.LegacyBillingCycleColumn > 0
                ? worksheet.Cell(rowNumber, header.LegacyBillingCycleColumn).GetString().Trim()
                : string.Empty;
            var routeValues = new List<RouteMatrixCell>();

            foreach (var routeColumn in header.RouteColumns)
            {
                var annualPrice = ReadAnnualPrice(worksheet.Cell(rowNumber, routeColumn.PriceColumn));
                BillingFrequency? term = null;
                if (routeColumn.BillingColumn > 0)
                {
                    var billingText = worksheet.Cell(rowNumber, routeColumn.BillingColumn).GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(billingText))
                    {
                        try
                        {
                            term = AnnualBillingHelper.ParseBillingCycle(billingText);
                        }
                        catch (FormatException ex)
                        {
                            throw new InvalidOperationException(
                                $"Row {rowNumber}, {routeColumn.RouteName}: {ex.Message}", ex);
                        }
                    }
                }

                if (term == null && !string.IsNullOrWhiteSpace(legacyBillingCycleText))
                {
                    try
                    {
                        term = AnnualBillingHelper.ParseBillingCycle(legacyBillingCycleText);
                    }
                    catch (FormatException ex)
                    {
                        throw new InvalidOperationException($"Row {rowNumber}: {ex.Message}", ex);
                    }
                }

                routeValues.Add(new RouteMatrixCell(routeColumn.RouteName, annualPrice, term));
            }

            if (routeValues.All(v => v.AnnualPrice <= 0))
            {
                result.SkippedRows++;
                continue;
            }

            foreach (var cell in routeValues.Where(v => v.AnnualPrice > 0))
            {
                if (cell.Term == null)
                    throw new InvalidOperationException(
                        $"Row {rowNumber}, {cell.RouteName}: billing cycle is required when a route price is entered.");
            }

            var customer = await _context.Customers
                .Include(c => c.CustomerRoutes)
                .Include(c => c.Contracts)
                    .ThenInclude(b => b.ContractRoutes)
                .FirstOrDefaultAsync(c => c.CustomerName.ToLower() == customerName.ToLower(), cancellationToken);

            if (customer == null)
            {
                customer = new Customer
                {
                    CustomerName = customerName,
                    Status = CustomerStatus.Active
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync(cancellationToken);

                _context.Contacts.Add(new Contact
                {
                    CustomerId = customer.Id,
                    Name = customerName,
                    Role = ContactRole.Billing
                });
                await _context.SaveChangesAsync(cancellationToken);
                result.CustomersAdded++;
            }
            else
            {
                result.CustomersUpdated++;
            }

            var activeRouteIds = new HashSet<int>();
            foreach (var cell in routeValues.Where(v => v.AnnualPrice > 0))
            {
                if (!routeLookup.TryGetValue(NormalizeName(cell.RouteName), out var route))
                    continue;

                activeRouteIds.Add(route.Id);
                var term = cell.Term!.Value;
                var ratePerMonth = AnnualBillingHelper.ToRatePerMonth(cell.AnnualPrice);

                var customerRoute = customer.CustomerRoutes
                    .FirstOrDefault(cr => cr.RouteId == route.Id);

                if (customerRoute == null)
                {
                    customerRoute = new CustomerRoute
                    {
                        CustomerId = customer.Id,
                        RouteId = route.Id,
                        AllStops = true,
                        Status = CustomerRouteStatus.Active,
                        BillingTerm = term
                    };
                    _context.CustomerRoutes.Add(customerRoute);
                    customer.CustomerRoutes.Add(customerRoute);
                    result.AssignmentsAdded++;
                }
                else
                {
                    customerRoute.AllStops = true;
                    customerRoute.Status = CustomerRouteStatus.Active;
                    customerRoute.BillingTerm = term;
                    result.AssignmentsUpdated++;
                }

                customerRoute.RatePerMonth = ratePerMonth;
                customerRoute.SubscribedMonthMask = SubscribedMonths.AllMonthsMask;
            }

            var removeAssignments = customer.CustomerRoutes
                .Where(cr => header.RouteColumns.Any(rc => routeLookup.TryGetValue(NormalizeName(rc.RouteName), out var route) && route.Id == cr.RouteId))
                .Where(cr => !activeRouteIds.Contains(cr.RouteId))
                .ToList();

            foreach (var assignment in removeAssignments)
            {
                _context.CustomerRoutes.Remove(assignment);
                result.AssignmentsRemoved++;
            }

            await _context.SaveChangesAsync(cancellationToken);

            foreach (var billing in customer.Contracts.ToList())
            {
                _context.CustomerContractRoutes.RemoveRange(billing.ContractRoutes);
                _context.CustomerContracts.Remove(billing);
            }

            if (activeRouteIds.Count > 0)
            {
                foreach (var termGroup in routeValues.Where(v => v.AnnualPrice > 0).GroupBy(v => v.Term!.Value))
                {
                    var billing = new CustomerContract
                    {
                        CustomerId = customer.Id,
                        ContractName = $"{BillingTermDisplay.Label(termGroup.Key)} Contract",
                        Term = termGroup.Key,
                        BillingAnchorMonth = 1,
                        ServiceMonthMask = SubscribedMonths.AllMonthsMask,
                        NextBillDate = new DateOnly(DateTime.Today.Year, 1, 1)
                    };
                    _context.CustomerContracts.Add(billing);
                    await _context.SaveChangesAsync(cancellationToken);

                    foreach (var cell in termGroup)
                    {
                        if (!routeLookup.TryGetValue(NormalizeName(cell.RouteName), out var route))
                            continue;

                        _context.CustomerContractRoutes.Add(new CustomerContractRoute
                        {
                            CustomerContractId = billing.Id,
                            RouteId = route.Id,
                            BillingAmount = AnnualBillingHelper.ToBillingPeriodAmount(cell.AnnualPrice, termGroup.Key)
                        });
                    }

                    result.ContractsConfigured++;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private static void WriteInstructionsSheet(XLWorkbook workbook)
    {
        var instructions = workbook.Worksheets.Add("Instructions");
        instructions.Cell(1, 1).Value = "Customer Route Matrix Import";
        instructions.Cell(1, 1).Style.Font.Bold = true;
        instructions.Cell(3, 1).Value = "Layout";
        instructions.Cell(4, 1).Value = "• Customers are listed down the first column.";
        instructions.Cell(5, 1).Value = "• Routes are listed across the top row.";
        instructions.Cell(6, 1).Value = "• Enter the annual price for each customer/route combination.";
        instructions.Cell(7, 1).Value = "• Enter the billing cycle beside each route (Monthly, Quarterly, or Annual).";
        instructions.Cell(8, 1).Value = "• Leave a route price blank when the customer is not on that route.";
        instructions.Cell(10, 1).Value = "Billing Cycle values";
        instructions.Cell(11, 1).Value = "Monthly, Quarterly, or Annual (M, Q, and Y also work).";
        instructions.Cell(13, 1).Value = "Example";
        instructions.Cell(14, 1).Value = CustomerColumn;
        instructions.Cell(14, 2).Value = "Exit - Central OH";
        instructions.Cell(14, 3).Value = GetRouteBillingColumnName("Exit - Central OH");
        instructions.Cell(14, 4).Value = "Exit - Louisville";
        instructions.Cell(14, 5).Value = GetRouteBillingColumnName("Exit - Louisville");
        instructions.Cell(15, 1).Value = "Example Visitor Center";
        instructions.Cell(15, 2).Value = 1200;
        instructions.Cell(15, 3).Value = "Quarterly";
        instructions.Cell(15, 4).Value = 800;
        instructions.Cell(15, 5).Value = "Annual";
        instructions.Columns().AdjustToContents();
    }

    private static MatrixHeader ReadHeaderRow(IXLWorksheet worksheet)
    {
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var customerColumn = 0;
        var legacyBillingCycleColumn = 0;
        var headersByColumn = new Dictionary<int, string>();

        for (var col = 1; col <= lastColumn; col++)
        {
            var header = worksheet.Cell(1, col).GetString().Trim();
            if (string.IsNullOrWhiteSpace(header))
                continue;

            headersByColumn[col] = header;

            if (header.Equals(CustomerColumn, StringComparison.OrdinalIgnoreCase))
            {
                customerColumn = col;
                continue;
            }

            if (header.Equals(LegacyBillingCycleColumn, StringComparison.OrdinalIgnoreCase))
            {
                legacyBillingCycleColumn = col;
            }
        }

        var routeColumns = new List<RouteColumnHeader>();
        foreach (var (col, header) in headersByColumn.OrderBy(h => h.Key))
        {
            if (col == customerColumn || col == legacyBillingCycleColumn)
                continue;

            if (header.EndsWith(BillingColumnSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsStandardRoute(header))
                continue;

            var billingHeader = GetRouteBillingColumnName(header);
            var billingColumn = headersByColumn
                .FirstOrDefault(h => h.Value.Equals(billingHeader, StringComparison.OrdinalIgnoreCase))
                .Key;

            routeColumns.Add(new RouteColumnHeader(col, billingColumn, header));
        }

        return new MatrixHeader(customerColumn, legacyBillingCycleColumn, routeColumns);
    }

    private static decimal ReadAnnualPrice(IXLCell cell)
    {
        if (cell.IsEmpty())
            return 0m;

        if (cell.TryGetValue(out decimal decimalValue))
            return decimalValue;

        if (cell.TryGetValue(out double doubleValue))
            return Convert.ToDecimal(doubleValue);

        var text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text))
            return 0m;

        text = text.TrimStart('$').Replace(",", string.Empty);
        return decimal.TryParse(text, out var parsed) ? parsed : 0m;
    }

    private static string NormalizeName(string name) => name.Trim();

    private sealed record MatrixHeader(int CustomerColumn, int LegacyBillingCycleColumn, IReadOnlyList<RouteColumnHeader> RouteColumns);

    private sealed record RouteColumnHeader(int PriceColumn, int BillingColumn, string RouteName);

    private sealed record RouteMatrixCell(string RouteName, decimal AnnualPrice, BillingFrequency? Term);

    private sealed record SampleMatrixRow(string CustomerName, BillingFrequency Term, decimal[] AnnualPrices);

    private sealed class CustomerMatrixExportData
    {
        private readonly Dictionary<int, CustomerRoute> _routesByRouteId;

        public CustomerMatrixExportData(Customer customer)
        {
            _routesByRouteId = customer.CustomerRoutes.ToDictionary(cr => cr.RouteId);
            _billingRoutes = customer.Contracts                .SelectMany(b => b.ContractRoutes.Select(cbr => (b.Term, cbr)))
                .GroupBy(x => x.cbr.RouteId)
                .ToDictionary(g => g.Key, g => g.First());
        }

        private readonly Dictionary<int, (BillingFrequency Term, CustomerContractRoute BillingRoute)> _billingRoutes;

        public decimal? GetAnnualPrice(int routeId)
        {
            if (_routesByRouteId.TryGetValue(routeId, out var customerRoute) && customerRoute.RatePerMonth > 0)
                return Math.Round(customerRoute.RatePerMonth * 12m, 2, MidpointRounding.AwayFromZero);

            if (_billingRoutes.TryGetValue(routeId, out var billingRoute) && billingRoute.BillingRoute.BillingAmount > 0)
                return AnnualBillingHelper.ToAnnualPrice(billingRoute.BillingRoute.BillingAmount, billingRoute.Term);

            return null;
        }

        public BillingFrequency? GetBillingTerm(int routeId)
        {
            if (_routesByRouteId.TryGetValue(routeId, out var customerRoute))
                return customerRoute.BillingTerm;

            if (_billingRoutes.TryGetValue(routeId, out var billingRoute))
                return billingRoute.Term;

            return null;
        }
    }
}

public class CustomerRouteMatrixImportResult
{
    public int CustomersAdded { get; set; }
    public int CustomersUpdated { get; set; }
    public int AssignmentsAdded { get; set; }
    public int AssignmentsUpdated { get; set; }
    public int AssignmentsRemoved { get; set; }
    public int ContractsConfigured { get; set; }
    public int SkippedRows { get; set; }
}
