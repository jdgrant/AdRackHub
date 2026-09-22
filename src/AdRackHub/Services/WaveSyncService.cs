using System.Net.Http.Json;
using System.Text.Json;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdRackHub.Services;

public class WaveSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ApplicationDbContext _context;
    private readonly WaveApiService _waveApiService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WaveSyncOptions _options;
    private readonly ILogger<WaveSyncService> _logger;

    public WaveSyncService(
        ApplicationDbContext context,
        WaveApiService waveApiService,
        IHttpClientFactory httpClientFactory,
        IOptions<WaveSyncOptions> options,
        ILogger<WaveSyncService> logger)
    {
        _context = context;
        _waveApiService = waveApiService;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsInboundConfigured => !string.IsNullOrWhiteSpace(_options.InboundWebhookSecret);
    public bool IsOutboundConfigured => !string.IsNullOrWhiteSpace(_options.OutboundWebhookUrl);
    public bool IsTestZapierConfigured => !string.IsNullOrWhiteSpace(_options.TestZapierWebhookUrl);
    public bool IsTestMakeConfigured => !string.IsNullOrWhiteSpace(_options.TestMakeWebhookUrl);
    public bool IsCreateOnSendConfigured => IsTestMakeConfigured || IsTestZapierConfigured;

    public async Task<WaveSyncWebhookResponse> ImportCustomerFromWaveAsync(
        InboundWaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var waveCustomerId = request.WaveCustomerId.Trim();
        if (!WaveApiService.IsLikelyWaveCustomerId(waveCustomerId))
            return WaveSyncWebhookResponse.Fail("waveCustomerId does not look like a valid Wave customer ID.");

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return WaveSyncWebhookResponse.Fail("name is required.");

        var existing = await _context.Customers
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.WaveCustomerId == waveCustomerId, cancellationToken);

        if (existing != null)
            return WaveSyncWebhookResponse.Ok(
                $"Customer already linked to Wave ID {waveCustomerId}.",
                existing.Id,
                waveCustomerId,
                created: false);

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var byEmail = await _context.Customers
                .Include(c => c.Contacts)
                .Where(c => c.Contacts.Any(ct => ct.Email == request.Email))
                .FirstOrDefaultAsync(cancellationToken);

            if (byEmail != null)
            {
                byEmail.WaveCustomerId = waveCustomerId;
                await _context.SaveChangesAsync(cancellationToken);
                return WaveSyncWebhookResponse.Ok(
                    $"Linked existing customer {byEmail.CustomerName} to Wave.",
                    byEmail.Id,
                    waveCustomerId,
                    created: false);
            }
        }

        var customer = new Customer
        {
            CustomerName = name,
            WaveCustomerId = waveCustomerId,
            Status = CustomerStatus.Active
        };

        if (!string.IsNullOrWhiteSpace(request.Email)
            || !string.IsNullOrWhiteSpace(request.Phone)
            || !string.IsNullOrWhiteSpace(request.Address))
        {
            customer.Contacts.Add(new Contact
            {
                Name = name,
                Email = request.Email?.Trim(),
                Phone = request.Phone?.Trim(),
                Address = request.Address?.Trim(),
                City = request.City?.Trim(),
                State = request.State?.Trim(),
                Zip = request.Zip?.Trim(),
                Role = ContactRole.Primary
            });
        }

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);

        return WaveSyncWebhookResponse.Ok(
            $"Created customer {customer.CustomerName} from Wave.",
            customer.Id,
            waveCustomerId,
            created: true);
    }

    public async Task<WaveSyncWebhookResponse> RecordContractCallbackAsync(
        InboundWaveContractCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var contract = await _context.CustomerContracts
            .FirstOrDefaultAsync(c => c.Id == request.CustomerContractId, cancellationToken);

        if (contract == null)
            return WaveSyncWebhookResponse.Fail($"Customer contract {request.CustomerContractId} not found.");

        if (!string.IsNullOrWhiteSpace(request.WaveRecurringInvoiceId))
            contract.WaveRecurringInvoiceId = request.WaveRecurringInvoiceId.Trim();

        await _context.SaveChangesAsync(cancellationToken);

        return WaveSyncWebhookResponse.ContractOk(
            request.Message ?? "Contract callback recorded.",
            contract.Id,
            contract.WaveRecurringInvoiceId);
    }

    public async Task<WaveSyncWebhookResponse> RecordInvoiceCallbackAsync(
        InboundWaveInvoiceCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _context.BillingRunInvoices
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken);

        if (invoice == null)
            return WaveSyncWebhookResponse.Fail($"Invoice {request.InvoiceId} not found.");

        if (!string.IsNullOrWhiteSpace(request.WaveInvoiceNumber))
            invoice.WaveInvoiceNumber = request.WaveInvoiceNumber.Trim();

        if (!string.IsNullOrWhiteSpace(request.WaveInvoiceId))
            invoice.WaveInvoiceId = request.WaveInvoiceId.Trim();

        if (!string.IsNullOrWhiteSpace(request.WaveInvoiceUrl))
            invoice.WaveInvoiceUrl = request.WaveInvoiceUrl.Trim();

        if (!invoice.HasWaveInvoiceNumber)
            return WaveSyncWebhookResponse.Fail("waveInvoiceNumber is required for invoice status updates.");

        var status = BillingRunInvoiceStatus.Submitted;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!WaveInvoiceStatuses.TryParse(request.Status, out status))
                return WaveSyncWebhookResponse.Fail("Status must be Submitted, Received, or Canceled.");
        }

        invoice.Status = status;
        invoice.ErrorMessage = null;

        if (status == BillingRunInvoiceStatus.Received)
            invoice.ReceivedDate = request.ReceivedDate ?? DateOnly.FromDateTime(DateTime.Today);
        else if (status == BillingRunInvoiceStatus.Submitted)
            invoice.ReceivedDate = null;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Invoice callback: invoice {InvoiceId} Wave #{WaveInvoiceNumber} status {Status}.",
            invoice.Id,
            invoice.WaveInvoiceNumber,
            invoice.Status);

        return WaveSyncWebhookResponse.InvoiceOk(
            request.Message ?? $"Invoice {invoice.Id} updated.",
            invoice.Id,
            invoice.WaveInvoiceNumber);
    }

    public Task PushContractToWaveAsync(int customerContractId, CancellationToken cancellationToken = default) =>
        PushBillingToWaveAsync(customerContractId, cancellationToken);

    public async Task PushCustomerToWaveAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

        if (customer == null)
            return;

        if (_options.PushCustomersToWaveApi && _waveApiService.IsConfigured)
            await EnsureCustomerOnWaveAsync(customer, cancellationToken);

        await SendOutboundWebhookAsync(BuildCustomerPayload(customer), cancellationToken);
    }

    public async Task PushBillingToWaveAsync(int customerContractId, CancellationToken cancellationToken = default)
    {
        var contract = await InvoiceSyncQuery(track: true)
            .FirstOrDefaultAsync(c => c.Id == customerContractId, cancellationToken);

        if (contract == null)
            return;

        if (_options.PushCustomersToWaveApi && _waveApiService.IsConfigured)
            await EnsureCustomerOnWaveAsync(contract.Customer, cancellationToken);

        await SendOutboundWebhookAsync(BuildContractPayload(contract), cancellationToken);
    }

    public async Task<(string? WaveCustomerId, string? ErrorMessage)> EnsureCustomerOnWaveAsync(
        Customer customer,
        CancellationToken cancellationToken = default)
    {
        if (WaveApiService.IsLikelyWaveCustomerId(customer.WaveCustomerId))
            return (customer.WaveCustomerId, null);

        if (!_waveApiService.IsConfigured)
            return (null, "Wave API is not configured.");

        var contact = customer.Contacts
            .OrderBy(c => c.Role == ContactRole.Billing ? 0 : c.Role == ContactRole.Primary ? 1 : 2)
            .ThenBy(c => c.Name)
            .FirstOrDefault();

        var (firstName, lastName) = ResolvePersonName(contact);

        var result = await _waveApiService.CreateCustomerAsync(new WaveCustomerRequest
        {
            Name = customer.CustomerName,
            FirstName = firstName,
            LastName = lastName,
            Email = FirstNonEmpty(customer.Email, contact?.Email),
            AddressLine1 = FirstNonEmpty(customer.Address, contact?.Address),
            City = FirstNonEmpty(customer.City, contact?.City),
            StateCode = FirstNonEmpty(customer.State, contact?.State),
            PostalCode = FirstNonEmpty(customer.Zip, contact?.Zip)
        }, cancellationToken);

        if (!result.Success)
            return (null, result.ErrorMessage);

        customer.WaveCustomerId = result.WaveCustomerId;
        await _context.SaveChangesAsync(cancellationToken);
        return (result.WaveCustomerId, null);
    }

    private static WaveOutboundCustomerPayload BuildCustomerPayload(Customer customer)
    {
        var contact = customer.Contacts
            .OrderBy(c => c.Role == ContactRole.Billing ? 0 : c.Role == ContactRole.Primary ? 1 : 2)
            .ThenBy(c => c.Name)
            .FirstOrDefault();

        return new WaveOutboundCustomerPayload
        {
            CustomerId = customer.Id,
            CustomerName = customer.CustomerName,
            WaveCustomerId = customer.WaveCustomerId,
            Status = customer.Status.ToString(),
            PrimaryContact = contact == null
                ? null
                : BuildContactPayload(contact, customer)
        };
    }

    private static WaveOutboundBillingPayload BuildContractPayload(CustomerContract contract)
    {
        var lines = contract.ContractRoutes
            .OrderBy(cr => cr.Route.RouteName)
            .Select(cr => BuildInvoiceLine(contract, cr.Route, AnnualBillingHelper.GetBillingAmount(cr), contract.Customer))
            .ToList();

        return new WaveOutboundBillingPayload
        {
            CustomerContractId = contract.Id,
            CustomerId = contract.CustomerId,
            CustomerName = contract.Customer.CustomerName,
            WaveCustomerId = contract.Customer.WaveCustomerId,
            ContractName = contract.ContractName,
            Term = BillingTermDisplay.Label(contract),
            BillingAnchorMonth = contract.BillingAnchorMonth,
            NextBillDate = contract.NextBillDate,
            ServiceMonthMask = contract.ServiceMonthMask,
            ContractEndDate = contract.ContractEndDate,
            TotalAmount = lines.Sum(l => l.UnitPrice * l.Quantity),
            LineItems = lines,
            Schedule = WaveRecurringScheduleMapper.Map(AnnualBillingHelper.BillingMonths(contract), contract.BillingAnchorMonth)
        };
    }

    /// <summary>
    /// Sends one contract's due invoice to Zapier (preferred) or Make, including the local invoice ID.
    /// </summary>
    public async Task<(bool Success, string Message, string? RemoteInvoiceId)> SendCreateOnSendInvoiceAsync(
        int year,
        int month,
        int customerId,
        int customerContractId,
        int billingRunInvoiceId,
        CancellationToken cancellationToken = default)
    {
        string destination;
        string? configuredUrl;
        string configKey;
        if (IsTestZapierConfigured)
        {
            destination = "Zapier";
            configuredUrl = _options.TestZapierWebhookUrl;
            configKey = "WaveSync:TestZapierWebhookUrl";
        }
        else if (IsTestMakeConfigured)
        {
            destination = "Make";
            configuredUrl = _options.TestMakeWebhookUrl;
            configKey = "WaveSync:TestMakeWebhookUrl";
        }
        else
        {
            return (false, "Configure WaveSync:TestZapierWebhookUrl or WaveSync:TestMakeWebhookUrl for Create On Send.", null);
        }

        var (success, message) = await SendDueInvoicesWebhookAsync(
            destination,
            configuredUrl,
            configKey,
            year,
            month,
            customerId,
            customerContractId,
            new Dictionary<int, int> { [customerId] = billingRunInvoiceId },
            test: false,
            cancellationToken);

        if (!success)
            return (false, message, null);

        _logger.LogInformation(
            "Create On Send logged invoice ID {InvoiceId} for contract {ContractId} / customer {CustomerId} ({Period}) via {Destination}.",
            billingRunInvoiceId,
            customerContractId,
            customerId,
            BillingDueCalculator.PeriodLabel(year, month),
            destination);

        return (true, $"{message} Invoice ID: {billingRunInvoiceId}.", billingRunInvoiceId.ToString());
    }

    private async Task<(bool Success, string Message)> SendDueInvoicesWebhookAsync(
        string destination,
        string? configuredUrl,
        string configKey,
        int year,
        int month,
        int? customerId,
        int? customerContractId,
        IReadOnlyDictionary<int, int>? invoiceIdByCustomer,
        bool test,
        CancellationToken cancellationToken)
    {
        var url = NormalizeWebhookUrl(configuredUrl);
        if (string.IsNullOrWhiteSpace(url))
            return (false, $"{configKey} is not configured.");

        var query = InvoiceSyncQuery(track: false)
            .Where(c => c.Customer.Status == CustomerStatus.Active && c.Customer.Type == CustomerType.Customer);

        if (customerContractId.HasValue)
            query = query.Where(c => c.Id == customerContractId.Value);
        else if (customerId.HasValue)
            query = query.Where(c => c.CustomerId == customerId.Value);

        var contracts = await query
            .Where(c => c.ContractRoutes.Any())
            .ToListAsync(cancellationToken);

        var dueItems = contracts
            .Where(c => BillingDueCalculator.IsContractDue(c, year, month))
            .SelectMany(c => c.ContractRoutes.Select(cr => new
            {
                Contract = c,
                Customer = c.Customer,
                Route = cr.Route,
                Amount = AnnualBillingHelper.GetBillingAmount(cr)
            }))
            .ToList();

        if (dueItems.Count == 0)
            return (false, $"No invoices due for {BillingDueCalculator.PeriodLabel(year, month)}"
                + (customerContractId == null
                    ? (customerId == null ? "." : $" for customer {customerId}.")
                    : $" for contract {customerContractId}."));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var periodLabel = BillingDueCalculator.PeriodLabel(year, month);
        var sent = 0;
        var errors = new List<string>();

        foreach (var group in dueItems.GroupBy(i => i.Customer.Id).OrderBy(g => g.First().Customer.CustomerName))
        {
            var customer = group.First().Customer;
            var contact = ResolveBillingOrDefaultContact(customer);
            var (firstName, lastName) = ResolvePersonName(contact);
            var invoiceRecipients = ResolveInvoiceRecipients(customer);
            var lineItems = group
                .OrderBy(i => i.Contract.ContractName)
                .ThenBy(i => i.Route.RouteName)
                .Select(i => BuildInvoiceLine(i.Contract, i.Route, i.Amount, i.Customer))
                .ToList();

            int? invoiceId = null;
            if (invoiceIdByCustomer != null && invoiceIdByCustomer.TryGetValue(customer.Id, out var mappedId))
                invoiceId = mappedId;

            var address = ResolveAddressParts(customer, contact);
            var payload = new WaveZapierTestInvoicePayload
            {
                Event = "invoice.due",
                Test = test,
                Year = year,
                Month = month,
                PeriodLabel = periodLabel,
                InvoiceId = invoiceId,
                CustomerId = customer.Id,
                CustomerName = customer.CustomerName,
                WaveCustomerId = customer.WaveCustomerId,
                Email = FirstNonEmpty(contact?.Email, customer.Email) ?? string.Empty,
                FirstName = firstName,
                LastName = lastName,
                ContactName = FirstNonEmpty(contact?.PersonName, contact?.Name),
                BusinessName = contact?.Name,
                ContactRole = contact?.Role.ToString(),
                Phone = FirstNonEmpty(contact?.Phone, customer.Phone),
                CellPhone = contact?.CellPhone,
                Address = address.Address,
                City = address.City,
                State = address.State,
                StateCode = address.StateCode,
                StateName = address.StateName,
                Region = address.Region,
                Province = address.Province,
                ProvinceCode = address.ProvinceCode,
                Country = address.Country,
                CountryCode = address.CountryCode,
                Zip = address.Zip,
                InvoiceRecipients = invoiceRecipients,
                InvoiceTitle = $"{customer.CustomerName} — {lineItems.FirstOrDefault()?.ServiceStartDate:MMM d, yyyy}–{lineItems.FirstOrDefault()?.ServiceEndDate:MMM d, yyyy}",
                InvoiceMemo = lineItems.Count == 0
                    ? $"AdRackHub billing for {periodLabel}. Amounts are period totals, not monthly rates."
                    : $"AdRackHub billing {lineItems.Min(l => l.ServiceStartDate):MMM d, yyyy}–{lineItems.Max(l => l.ServiceEndDate):MMM d, yyyy}. Amounts are period totals, not monthly rates.",
                Currency = "USD",
                InvoiceDate = today,
                DueDate = today.AddDays(30),
                ServiceStartDate = lineItems.Count == 0 ? null : lineItems.Min(l => l.ServiceStartDate),
                ServiceEndDate = lineItems.Count == 0 ? null : lineItems.Max(l => l.ServiceEndDate),
                PriceNote = "Line item amounts are period totals, not monthly rates.",
                TotalAmount = lineItems.Sum(l => l.UnitPrice * l.Quantity),
                LineItems = lineItems
            };

            try
            {
                var client = _httpClientFactory.CreateClient();
                using var response = await client.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "{Destination} due-invoice webhook returned {StatusCode} for customer {CustomerId}: {Body}",
                        destination,
                        (int)response.StatusCode,
                        customer.Id,
                        body);
                    errors.Add($"{customer.CustomerName}: {(int)response.StatusCode}");
                    continue;
                }

                if (invoiceId.HasValue)
                {
                    _logger.LogInformation(
                        "{Destination} due-invoice webhook accepted for customer {CustomerId}; invoice ID {InvoiceId}. Response: {Body}",
                        destination,
                        customer.Id,
                        invoiceId.Value,
                        string.IsNullOrWhiteSpace(body) ? "(empty)" : body);
                }

                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send {Destination} due-invoice webhook for customer {CustomerId}.", destination, customer.Id);
                errors.Add($"{customer.CustomerName}: {ex.Message}");
            }
        }

        if (sent == 0)
            return (false, $"{destination} send failed. {string.Join("; ", errors)}");

        var summary = $"Sent {sent} due invoice(s) for {periodLabel} to {destination}.";
        if (errors.Count > 0)
            summary += $" Some failed: {string.Join("; ", errors)}";
        return (true, summary);
    }

    private IQueryable<CustomerContract> InvoiceSyncQuery(bool track)
    {
        IQueryable<CustomerContract> query = _context.CustomerContracts
            .AsSplitQuery()
            .Include(c => c.Customer)
                .ThenInclude(c => c.Contacts)
            .Include(c => c.Customer)
                .ThenInclude(c => c.CustomerRoutes)
                    .ThenInclude(cr => cr.CustomerRouteStops)
                        .ThenInclude(crs => crs.Stop)
            .Include(c => c.ContractRoutes)
                .ThenInclude(cr => cr.Route)
                    .ThenInclude(r => r.Stops);

        return track ? query : query.AsNoTracking();
    }

    /// <summary>
    /// Accepts full https URLs or Make-style "id@hook.us2.make.com" values.
    /// </summary>
    private static string? NormalizeWebhookUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        var at = trimmed.IndexOf('@');
        if (at > 0 && at < trimmed.Length - 1)
        {
            var id = trimmed[..at];
            var host = trimmed[(at + 1)..];
            return $"https://{host}/{id}";
        }

        return trimmed;
    }

    private static Contact? ResolveBillingOrDefaultContact(Customer customer) =>
        customer.Contacts
            .OrderBy(c => c.SendInvoice ? 0 : 1)
            .ThenBy(c => c.Role == ContactRole.Billing ? 0 : c.Role == ContactRole.Primary ? 1 : 2)
            .ThenBy(c => c.Name)
            .FirstOrDefault();

    private static IReadOnlyList<WaveOutboundContactPayload> ResolveInvoiceRecipients(Customer customer)
    {
        var recipients = customer.Contacts
            .Where(c => c.SendInvoice)
            .OrderBy(c => c.Role == ContactRole.Billing ? 0 : c.Role == ContactRole.Primary ? 1 : 2)
            .ThenBy(c => c.Name)
            .Select(c => BuildContactPayload(c, customer, sendInvoice: true))
            .ToList();

        // Fall back to billing/primary contact so invoices still have a recipient.
        if (recipients.Count == 0)
        {
            var fallback = ResolveBillingOrDefaultContact(customer);
            if (fallback != null)
                recipients.Add(BuildContactPayload(fallback, customer, sendInvoice: false));
        }

        return recipients;
    }

    private static WaveOutboundBillingLinePayload BuildInvoiceLine(
        CustomerContract contract,
        Models.Route route,
        decimal amount,
        Customer customer)
    {
        var content = InvoiceLineFormatter.Build(
            route.RouteName,
            contract.NextBillDate,
            AnnualBillingHelper.BillingMonths(contract),
            amount,
            customer,
            route);

        return new WaveOutboundBillingLinePayload
        {
            Description = content.Description,
            Quantity = 1,
            UnitPrice = amount,
            RouteName = route.RouteName,
            Product = RouteProductHelper.Label(route.Product),
            WaveProductId = route.WaveProductId,
            ServiceStartDate = content.ServiceStartDate,
            ServiceEndDate = content.ServiceEndDate,
            SpaceCount = content.SpaceCount,
            Locations = content.Locations,
            LocationNames = content.LocationNames,
            PeriodMonthCount = content.PeriodMonthCount,
            PeriodAmount = content.PeriodAmount,
            MonthlyRate = content.MonthlyRate,
            PriceIsPeriodTotal = true,
            PriceLabel = content.PriceLabel
        };
    }

    private static (string? FirstName, string? LastName) ResolvePersonName(Contact? contact)
    {
        if (contact == null)
            return (null, null);

        if (!string.IsNullOrWhiteSpace(contact.FirstName) || !string.IsNullOrWhiteSpace(contact.LastName))
            return (NullIfWhiteSpace(contact.FirstName), NullIfWhiteSpace(contact.LastName));

        // Legacy contacts stored a person name in Name before Business Name existed.
        return SplitName(contact.Name);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task SendOutboundWebhookAsync(object payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.OutboundWebhookUrl))
            return;

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsJsonAsync(
                _options.OutboundWebhookUrl,
                payload,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Wave sync outbound webhook returned {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Wave sync outbound webhook.");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength] + "…";

    private static WaveOutboundContactPayload BuildContactPayload(
        Contact contact,
        Customer customer,
        bool? sendInvoice = null)
    {
        var (firstName, lastName) = ResolvePersonName(contact);
        var address = ResolveAddressParts(customer, contact);
        return new WaveOutboundContactPayload
        {
            ContactId = contact.Id,
            Name = FirstNonEmpty(contact.PersonName, contact.Name),
            FirstName = firstName,
            LastName = lastName,
            Email = contact.Email,
            Phone = contact.Phone,
            CellPhone = contact.CellPhone,
            Role = contact.Role.ToString(),
            SendInvoice = sendInvoice ?? contact.SendInvoice,
            Address = address.Address,
            City = address.City,
            State = address.State,
            StateCode = address.StateCode,
            StateName = address.StateName,
            Region = address.Region,
            Province = address.Province,
            ProvinceCode = address.ProvinceCode,
            Country = address.Country,
            CountryCode = address.CountryCode,
            Zip = address.Zip
        };
    }

    private static AddressParts ResolveAddressParts(Customer customer, Contact? contact)
    {
        var rawState = FirstNonEmpty(contact?.State, customer.State);
        var parsed = UsState.Parse(rawState);
        return new AddressParts(
            FirstNonEmpty(contact?.Address, customer.Address),
            FirstNonEmpty(contact?.City, customer.City),
            parsed?.Abbreviation ?? rawState ?? string.Empty,
            parsed?.Abbreviation ?? string.Empty,
            parsed?.Name ?? string.Empty,
            parsed?.Slug ?? string.Empty,
            parsed?.Slug ?? string.Empty,
            parsed?.ProvinceCode ?? string.Empty,
            "United States",
            "US",
            FirstNonEmpty(contact?.Zip, customer.Zip));
    }

    private readonly record struct AddressParts(
        string? Address,
        string? City,
        string State,
        string StateCode,
        string StateName,
        string Region,
        string Province,
        string ProvinceCode,
        string Country,
        string CountryCode,
        string? Zip);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static (string? FirstName, string? LastName) SplitName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return (null, null);

        var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (null, null),
            1 => (parts[0], null),
            _ => (parts[0], parts[1])
        };
    }
}
