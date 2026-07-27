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
        var contract = await _context.CustomerContracts
            .Include(c => c.Customer)
                .ThenInclude(c => c.Contacts)
            .Include(c => c.ContractRoutes)
                .ThenInclude(cr => cr.Route)
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

        var (firstName, lastName) = SplitName(contact?.Name);

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
                : new WaveOutboundContactPayload
                {
                    Name = contact.Name,
                    Email = contact.Email,
                    Phone = contact.Phone,
                    Address = contact.Address,
                    City = contact.City,
                    State = contact.State,
                    Zip = contact.Zip
                }
        };
    }

    private static WaveOutboundBillingPayload BuildContractPayload(CustomerContract contract)
    {
        var lines = contract.ContractRoutes
            .OrderBy(cr => cr.Route.RouteName)
            .Select(cr => new WaveOutboundBillingLinePayload
            {
                Description = $"{contract.ContractName} ({BillingTermDisplay.Label(contract.Term)}) — {cr.Route.RouteName}",
                Quantity = 1,
                UnitPrice = AnnualBillingHelper.GetBillingAmount(cr),
                RouteName = cr.Route.RouteName
            })
            .ToList();

        return new WaveOutboundBillingPayload
        {
            CustomerContractId = contract.Id,
            CustomerId = contract.CustomerId,
            CustomerName = contract.Customer.CustomerName,
            WaveCustomerId = contract.Customer.WaveCustomerId,
            ContractName = contract.ContractName,
            Term = BillingTermDisplay.Label(contract.Term),
            BillingAnchorMonth = contract.BillingAnchorMonth,
            NextBillDate = contract.NextBillDate,
            ServiceMonthMask = contract.ServiceMonthMask,
            ContractEndDate = contract.ContractEndDate,
            TotalAmount = lines.Sum(l => l.UnitPrice * l.Quantity),
            LineItems = lines,
            Schedule = WaveRecurringScheduleMapper.Map(contract.Term, contract.BillingAnchorMonth)
        };
    }

    private async Task SendOutboundWebhookAsync(object payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.OutboundWebhookUrl))
            return;

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(WaveSyncService));
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
