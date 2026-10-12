using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class AgentCustomerApiService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    private static readonly string[] CustomerReadOnly =
    {
        "id", "needsMoreInfo", "latitude", "longitude", "accountManager", "hubUrl", "brochureCode", "waveCustomerId"
    };

    private static readonly string[] ContactReadOnly = { "id", "customerId", "displayName" };

    private readonly ApplicationDbContext _context;
    private readonly BrochureLabelIdService _labels;
    private readonly CustomerGeocodeService _geocode;
    private readonly CustomerNeedsMoreInfoService _needsMoreInfo;
    private readonly WaveSyncService _wave;
    private readonly ILogger<AgentCustomerApiService> _logger;

    public AgentCustomerApiService(
        ApplicationDbContext context,
        BrochureLabelIdService labels,
        CustomerGeocodeService geocode,
        CustomerNeedsMoreInfoService needsMoreInfo,
        WaveSyncService wave,
        ILogger<AgentCustomerApiService> logger)
    {
        _context = context;
        _labels = labels;
        _geocode = geocode;
        _needsMoreInfo = needsMoreInfo;
        _wave = wave;
        _logger = logger;
    }

    public async Task<AgentCallResult<AgentCustomerListDto>> ListAsync(
        string? type,
        string? status,
        string? search,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var query = _context.Customers.AsNoTracking().AsQueryable();

        if (!TryApplyTypeFilter(query, type, out query, out var typeError))
            errors.Add(typeError!);
        if (!TryApplyStatusFilter(query, status, out query, out var statusError))
            errors.Add(statusError!);
        if (errors.Count > 0)
            return AgentCallResult<AgentCustomerListDto>.Invalid(errors);

        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize)
            pageSize = MaxPageSize;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 200)
                return AgentCallResult<AgentCustomerListDto>.Invalid(new List<string> { "search must be 200 characters or fewer." });

            int? idMatch = int.TryParse(term, out var parsedId) ? parsedId : null;
            query = query.Where(c =>
                c.CustomerName.Contains(term)
                || (idMatch != null && c.Id == idMatch)
                || (c.Address != null && c.Address.Contains(term))
                || (c.City != null && c.City.Contains(term))
                || (c.State != null && c.State.Contains(term))
                || (c.Zip != null && c.Zip.Contains(term))
                || (c.Phone != null && c.Phone.Contains(term))
                || (c.Email != null && c.Email.Contains(term))
                || (c.WebUrl != null && c.WebUrl.Contains(term))
                || c.Contacts.Any(ct =>
                    ct.Name.Contains(term)
                    || (ct.FirstName != null && ct.FirstName.Contains(term))
                    || (ct.LastName != null && ct.LastName.Contains(term))
                    || (ct.Title != null && ct.Title.Contains(term))
                    || (ct.Email != null && ct.Email.Contains(term))
                    || (ct.Phone != null && ct.Phone.Contains(term))
                    || (ct.CellPhone != null && ct.CellPhone.Contains(term))));
        }

        var total = await query.CountAsync(cancellationToken);
        var pageIds = await query
            .OrderBy(c => c.CustomerName)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var rows = await _context.Customers
            .AsNoTracking()
            .Where(c => pageIds.Contains(c.Id))
            .Include(c => c.Contacts)
            .Include(c => c.AccountManager)
            .ToListAsync(cancellationToken);
        var byId = rows.ToDictionary(c => c.Id);

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return AgentCallResult<AgentCustomerListDto>.Ok(new AgentCustomerListDto
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = totalPages,
            HasMore = page < totalPages,
            Items = pageIds.Where(byId.ContainsKey).Select(id => Map(byId[id], hubBase)).ToList()
        });
    }

    public async Task<AgentCallResult<AgentCustomerDto>> GetAsync(int id, string hubBase, CancellationToken cancellationToken)
    {
        var customer = await LoadAsync(id, tracking: false, cancellationToken);
        if (customer == null)
            return AgentCallResult<AgentCustomerDto>.Missing($"No customer or prospect with id {id}.");
        return AgentCallResult<AgentCustomerDto>.Ok(Map(customer, hubBase));
    }

    public async Task<AgentCallResult<AgentCustomerDto>> CreateAsync(JsonElement body, string hubBase, CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return AgentCallResult<AgentCustomerDto>.Invalid(new List<string> { "Request body must be a JSON object." });

        var errors = new List<string>();
        var customer = new Customer
        {
            Status = CustomerStatus.Active,
            InvoiceReceiptMethod = InvoiceReceiptMethod.Mail
        };
        var changed = TryApplyCustomer(customer, body, creating: true, errors);
        if (AgentJson.Has(body, "contact"))
            errors.Add("Send people in contacts, an array of contact objects.");

        var contacts = new List<Contact>();
        if (AgentJson.Try(body, "contacts", out var contactsEl))
        {
            if (contactsEl.ValueKind != JsonValueKind.Array)
                errors.Add("contacts must be an array.");
            else
            {
                var index = 0;
                foreach (var item in contactsEl.EnumerateArray())
                {
                    var label = $"contacts[{index}]";
                    if (item.ValueKind != JsonValueKind.Object)
                        errors.Add($"{label} must be an object.");
                    else
                    {
                        var before = errors.Count;
                        var contact = new Contact { Role = ContactRole.Primary };
                        TryApplyContact(contact, item, creating: true, customer.CustomerName, errors, label);
                        if (errors.Count == before)
                            contacts.Add(contact);
                    }
                    index++;
                }
            }
        }

        if (!changed && errors.Count == 0)
            errors.Add("customerName and type are required.");
        if (errors.Count > 0)
            return AgentCallResult<AgentCustomerDto>.Invalid(errors);

        ApplyTypeFlagRules(customer);
        customer.NeedsMoreInfo = CustomerContactCompleteness.NeedsMoreInfo(customer);
        foreach (var contact in contacts)
            customer.Contacts.Add(contact);

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);
        await FinishCustomerWriteAsync(
            customer,
            addressChanged: CustomerAddressHelper.HasGeocodableAddress(customer),
            pushWave: customer.Type == CustomerType.Customer,
            cancellationToken);

        _logger.LogInformation("Agent API created {Type} {CustomerId} {CustomerName}.", customer.Type, customer.Id, customer.CustomerName);
        return AgentCallResult<AgentCustomerDto>.Created(Map(customer, hubBase), $"{hubBase}/api/agent/customers/{customer.Id}");
    }

    public async Task<AgentCallResult<AgentCustomerDto>> UpdateAsync(int id, JsonElement body, string hubBase, CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return AgentCallResult<AgentCustomerDto>.Invalid(new List<string> { "Request body must be a JSON object." });

        var customer = await LoadAsync(id, tracking: true, cancellationToken);
        if (customer == null)
            return AgentCallResult<AgentCustomerDto>.Missing($"No customer or prospect with id {id}.");

        var errors = new List<string>();
        if (AgentJson.Has(body, "contacts") || AgentJson.Has(body, "contact"))
            errors.Add("Add a contact with POST /api/agent/customers/{id}/contacts or update one with PATCH /api/agent/customers/{id}/contacts/{contactId}.");

        var beforeAddress = SnapshotAddress(customer);
        var wasCustomer = customer.Type == CustomerType.Customer;
        var changed = TryApplyCustomer(customer, body, creating: false, errors);
        if (errors.Count > 0)
            return AgentCallResult<AgentCustomerDto>.Invalid(errors);
        if (!changed)
            return AgentCallResult<AgentCustomerDto>.Invalid(new List<string> { "Send at least one field to change." });

        ApplyTypeFlagRules(customer);
        customer.NeedsMoreInfo = CustomerContactCompleteness.NeedsMoreInfo(customer);
        var addressChanged = CustomerAddressHelper.AddressChanged(beforeAddress, customer);
        await _context.SaveChangesAsync(cancellationToken);
        await FinishCustomerWriteAsync(
            customer,
            addressChanged,
            pushWave: !wasCustomer && customer.Type == CustomerType.Customer,
            cancellationToken);

        _logger.LogInformation("Agent API updated customer {CustomerId}.", customer.Id);
        return AgentCallResult<AgentCustomerDto>.Ok(Map(customer, hubBase));
    }

    public async Task<AgentCallResult<AgentCustomerDto>> AddContactAsync(int customerId, JsonElement body, string hubBase, CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return AgentCallResult<AgentCustomerDto>.Invalid(new List<string> { "Request body must be a JSON object." });

        var customer = await LoadAsync(customerId, tracking: true, cancellationToken);
        if (customer == null)
            return AgentCallResult<AgentCustomerDto>.Missing($"No customer or prospect with id {customerId}.");

        var errors = new List<string>();
        var contact = new Contact
        {
            CustomerId = customer.Id,
            Role = ContactRole.Primary
        };
        var changed = TryApplyContact(contact, body, creating: true, customer.CustomerName, errors, null);
        if (errors.Count > 0)
            return AgentCallResult<AgentCustomerDto>.Invalid(errors);
        if (!changed)
            return AgentCallResult<AgentCustomerDto>.Invalid(new List<string> { "Send at least one contact field." });

        customer.Contacts.Add(contact);
        await _context.SaveChangesAsync(cancellationToken);
        await FinishContactWriteAsync(contact, addressChanged: CustomerAddressHelper.HasAnyAddressPart(contact.Address, contact.City, contact.State, contact.Zip), cancellationToken);

        _logger.LogInformation("Agent API added contact {ContactId} on customer {CustomerId}.", contact.Id, customer.Id);
        return AgentCallResult<AgentCustomerDto>.Created(Map(customer, hubBase), $"{hubBase}/api/agent/customers/{customer.Id}");
    }

    public async Task<AgentCallResult<AgentCustomerDto>> UpdateContactAsync(
        int customerId,
        int contactId,
        JsonElement body,
        string hubBase,
        CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return AgentCallResult<AgentCustomerDto>.Invalid(new List<string> { "Request body must be a JSON object." });

        var customer = await LoadAsync(customerId, tracking: true, cancellationToken);
        if (customer == null)
            return AgentCallResult<AgentCustomerDto>.Missing($"No customer or prospect with id {customerId}.");

        var contact = customer.Contacts.FirstOrDefault(c => c.Id == contactId);
        if (contact == null)
            return AgentCallResult<AgentCustomerDto>.Missing($"No contact {contactId} on customer or prospect {customerId}.");

        var errors = new List<string>();
        var beforeAddress = new Contact
        {
            Address = contact.Address,
            City = contact.City,
            State = contact.State,
            Zip = contact.Zip
        };
        var changed = TryApplyContact(contact, body, creating: false, customer.CustomerName, errors, null);
        if (errors.Count > 0)
            return AgentCallResult<AgentCustomerDto>.Invalid(errors);
        if (!changed)
            return AgentCallResult<AgentCustomerDto>.Invalid(new List<string> { "Send at least one field to change." });

        ContactTitleParser.ApplyFromLastName(contact);
        await _context.SaveChangesAsync(cancellationToken);
        await FinishContactWriteAsync(contact, CustomerAddressHelper.AddressChanged(beforeAddress, contact), cancellationToken);

        _logger.LogInformation("Agent API updated contact {ContactId} on customer {CustomerId}.", contact.Id, customer.Id);
        return AgentCallResult<AgentCustomerDto>.Ok(Map(customer, hubBase));
    }

    private async Task FinishCustomerWriteAsync(Customer customer, bool addressChanged, bool pushWave, CancellationToken cancellationToken)
    {
        if (customer.Type == CustomerType.Customer)
            await TryAssignLabelAsync(customer.Id, cancellationToken);
        if (addressChanged)
            await TryGeocodeAsync(customer.Id, cancellationToken);
        await _needsMoreInfo.RefreshAsync(customer.Id, cancellationToken);
        if (pushWave)
            await TryPushWaveAsync(customer.Id, cancellationToken);
    }

    private async Task FinishContactWriteAsync(Contact contact, bool addressChanged, CancellationToken cancellationToken)
    {
        await PromoteReceiptBothAsync(contact.CustomerId, cancellationToken);
        if (addressChanged)
            await TryGeocodeAsync(contact.CustomerId, cancellationToken);
        await _needsMoreInfo.RefreshAsync(contact.CustomerId, cancellationToken);
    }

    private async Task PromoteReceiptBothAsync(int customerId, CancellationToken cancellationToken)
    {
        var hasSendInvoiceEmail = await _context.Contacts.AnyAsync(ct =>
            ct.CustomerId == customerId
            && ct.SendInvoice
            && ct.Email != null
            && ct.Email.Trim() != "", cancellationToken);
        if (!hasSendInvoiceEmail)
            return;

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer == null || customer.InvoiceReceiptMethod == InvoiceReceiptMethod.Both)
            return;

        customer.InvoiceReceiptMethod = InvoiceReceiptMethod.Both;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task TryAssignLabelAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            await _labels.AssignIfMissingAsync(customerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent API brochure id assignment failed for customer {CustomerId}.", customerId);
        }
    }

    private async Task TryGeocodeAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            await _geocode.UpdateCoordinatesOnlyAsync(customerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent API geocode failed for customer {CustomerId}.", customerId);
        }
    }

    private async Task TryPushWaveAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            await _wave.PushCustomerToWaveAsync(customerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent API Wave push failed for customer {CustomerId}.", customerId);
        }
    }

    private Task<Customer?> LoadAsync(int id, bool tracking, CancellationToken cancellationToken)
    {
        var query = _context.Customers
            .Include(c => c.Contacts)
            .Include(c => c.AccountManager)
            .AsQueryable();
        if (!tracking)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    private static AgentCustomerDto Map(Customer customer, string hubBase)
    {
        return new AgentCustomerDto
        {
            Id = customer.Id,
            CustomerName = customer.CustomerName,
            Type = customer.Type.ToString(),
            Status = customer.Status.ToString(),
            IsHighValueProspect = customer.IsHighValueProspect,
            IsAtRisk = customer.IsAtRisk,
            NeedsMoreInfo = customer.NeedsMoreInfo,
            Address = customer.Address,
            City = customer.City,
            State = customer.State,
            Zip = customer.Zip,
            Latitude = customer.Latitude,
            Longitude = customer.Longitude,
            Phone = customer.Phone,
            Email = customer.Email,
            WebUrl = customer.WebUrl,
            InvoiceReceiptMethod = customer.InvoiceReceiptMethod.ToString(),
            AccountManager = AccountManagerName(customer.AccountManager),
            HubUrl = $"{hubBase}/Customers/Details/{customer.Id}",
            Contacts = customer.Contacts
                .OrderBy(c => c.Role == ContactRole.Primary ? 0 : 1)
                .ThenBy(c => c.Id)
                .Select(MapContact)
                .ToList()
        };
    }

    private static AgentContactDto MapContact(Contact contact) => new()
    {
        Id = contact.Id,
        CustomerId = contact.CustomerId,
        Name = contact.Name,
        FirstName = contact.FirstName,
        LastName = contact.LastName,
        Title = contact.Title,
        DisplayName = contact.DisplayName,
        Email = contact.Email,
        Phone = contact.Phone,
        CellPhone = contact.CellPhone,
        Address = contact.Address,
        City = contact.City,
        State = contact.State,
        Zip = contact.Zip,
        WebUrl = contact.WebUrl,
        LinkedInUrl = contact.LinkedInUrl,
        Role = contact.Role.ToString(),
        SendInvoice = contact.SendInvoice
    };

    private static string? AccountManagerName(ApplicationUser? user)
    {
        if (user == null)
            return null;
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            return user.DisplayName;
        if (!string.IsNullOrWhiteSpace(user.Email))
            return user.Email;
        return user.UserName;
    }

    private static void ApplyTypeFlagRules(Customer customer)
    {
        if (customer.Type == CustomerType.Customer)
            customer.IsHighValueProspect = false;
        else
            customer.IsAtRisk = false;
    }

    private static Customer SnapshotAddress(Customer customer) => new()
    {
        Address = customer.Address,
        City = customer.City,
        State = customer.State,
        Zip = customer.Zip
    };

    private static bool TryApplyCustomer(Customer customer, JsonElement body, bool creating, List<string> errors)
    {
        RejectReadOnly(body, errors, CustomerReadOnly);
        var changed = false;
        var highValueSent = false;
        var atRiskSent = false;

        if (TryReadString(body, "customerName", 200, errors, out var namePresent, out var name))
        {
            if (namePresent)
            {
                if (string.IsNullOrEmpty(name))
                    errors.Add("customerName is required.");
                else
                {
                    customer.CustomerName = name;
                    changed = true;
                }
            }
            else if (creating)
                errors.Add("customerName is required.");
        }

        if (TryReadString(body, "type", 40, errors, out var typePresent, out var typeText))
        {
            if (typePresent)
            {
                if (string.IsNullOrEmpty(typeText))
                    errors.Add("type must be Customer or Prospect.");
                else if (!TryParseCustomerType(typeText, out var type, out var typeError))
                    errors.Add(typeError ?? "type must be Customer or Prospect.");
                else
                {
                    customer.Type = type;
                    changed = true;
                }
            }
            else if (creating)
                errors.Add("type is required and must be Customer or Prospect.");
        }

        if (TryReadString(body, "status", 40, errors, out var statusPresent, out var statusText))
        {
            if (statusPresent)
            {
                if (string.IsNullOrEmpty(statusText) || !TryParseEnum<CustomerStatus>(statusText, out var status))
                    errors.Add("status must be Active or Inactive.");
                else
                {
                    customer.Status = status;
                    changed = true;
                }
            }
        }

        changed |= TryAssignOptional(body, "address", 300, errors, value => customer.Address = value);
        changed |= TryAssignOptional(body, "city", 100, errors, value => customer.City = value);
        changed |= TryAssignOptional(body, "state", 50, errors, value => customer.State = value);
        changed |= TryAssignOptional(body, "zip", 20, errors, value => customer.Zip = value);
        changed |= TryAssignPhone(body, "phone", errors, value => customer.Phone = value);
        changed |= TryAssignEmail(body, "email", 200, errors, value => customer.Email = value);
        changed |= TryAssignOptional(body, "webUrl", 500, errors, value => customer.WebUrl = value);

        if (TryReadString(body, "invoiceReceiptMethod", 40, errors, out var receiptPresent, out var receiptText))
        {
            if (receiptPresent)
            {
                if (string.IsNullOrEmpty(receiptText) || !TryParseEnum<InvoiceReceiptMethod>(receiptText, out var receipt))
                    errors.Add("invoiceReceiptMethod must be Mail, Email, or Both.");
                else
                {
                    customer.InvoiceReceiptMethod = receipt;
                    changed = true;
                }
            }
        }

        if (TryReadBool(body, "isHighValueProspect", errors, out highValueSent, out var highValue) && highValueSent)
        {
            customer.IsHighValueProspect = highValue;
            changed = true;
        }

        if (TryReadBool(body, "isAtRisk", errors, out atRiskSent, out var atRisk) && atRiskSent)
        {
            customer.IsAtRisk = atRisk;
            changed = true;
        }

        if (customer.Type == CustomerType.Customer && highValueSent && customer.IsHighValueProspect)
            errors.Add("isHighValueProspect applies to prospects only.");
        if (customer.Type != CustomerType.Customer && atRiskSent && customer.IsAtRisk)
            errors.Add("isAtRisk applies to customers only.");

        return changed;
    }

    private static bool TryApplyContact(
        Contact contact,
        JsonElement body,
        bool creating,
        string? fallbackName,
        List<string> errors,
        string? prefix)
    {
        RejectReadOnly(body, errors, ContactReadOnly, prefix);
        var changed = false;

        if (TryReadString(body, "name", 200, errors, out var namePresent, out var name, prefix))
        {
            if (namePresent)
            {
                if (string.IsNullOrEmpty(name))
                    errors.Add(Field(prefix, "name") + " is required.");
                else
                {
                    contact.Name = name;
                    changed = true;
                }
            }
            else if (creating)
            {
                var fallback = string.IsNullOrWhiteSpace(fallbackName) ? null : fallbackName.Trim();
                if (string.IsNullOrEmpty(fallback))
                    errors.Add(Field(prefix, "name") + " is required.");
                else
                    contact.Name = fallback.Length <= 200 ? fallback : fallback[..200];
            }
        }

        changed |= TryAssignOptional(body, "firstName", 100, errors, value => contact.FirstName = value, prefix);
        changed |= TryAssignOptional(body, "lastName", 100, errors, value => contact.LastName = value, prefix);
        changed |= TryAssignOptional(body, "title", 200, errors, value => contact.Title = value, prefix);
        changed |= TryAssignEmail(body, "email", 200, errors, value => contact.Email = value, prefix);
        changed |= TryAssignPhone(body, "phone", errors, value => contact.Phone = value, prefix);
        changed |= TryAssignPhone(body, "cellPhone", errors, value => contact.CellPhone = value, prefix);
        changed |= TryAssignOptional(body, "address", 300, errors, value => contact.Address = value, prefix);
        changed |= TryAssignOptional(body, "city", 100, errors, value => contact.City = value, prefix);
        changed |= TryAssignOptional(body, "state", 50, errors, value => contact.State = value, prefix);
        changed |= TryAssignOptional(body, "zip", 20, errors, value => contact.Zip = value, prefix);
        changed |= TryAssignOptional(body, "webUrl", 500, errors, value => contact.WebUrl = value, prefix);

        if (TryReadString(body, "linkedInUrl", 500, errors, out var linkedInPresent, out var linkedIn, prefix))
        {
            if (linkedInPresent)
            {
                if (string.IsNullOrEmpty(linkedIn))
                    contact.LinkedInUrl = null;
                else if (LinkedInSearch.NormalizeProfileUrl(linkedIn) == null)
                    errors.Add(Field(prefix, "linkedInUrl") + " must be a LinkedIn profile URL such as https://www.linkedin.com/in/name.");
                else
                    contact.LinkedInUrl = LinkedInSearch.NormalizeProfileUrl(linkedIn);
                changed = true;
            }
        }

        if (TryReadString(body, "role", 40, errors, out var rolePresent, out var roleText, prefix))
        {
            if (rolePresent)
            {
                if (string.IsNullOrEmpty(roleText) || !TryParseEnum<ContactRole>(roleText, out var role))
                    errors.Add(Field(prefix, "role") + " must be Primary, Billing, Operations, Marketing, or Other.");
                else
                {
                    contact.Role = role;
                    changed = true;
                }
            }
        }

        if (TryReadBool(body, "sendInvoice", errors, out var sendPresent, out var sendInvoice, prefix) && sendPresent)
        {
            contact.SendInvoice = sendInvoice;
            changed = true;
        }

        if (creating || AgentJson.Has(body, "lastName") || AgentJson.Has(body, "title"))
            ContactTitleParser.ApplyFromLastName(contact);

        return changed;
    }

    private static void RejectReadOnly(JsonElement body, List<string> errors, IEnumerable<string> names, string? prefix = null)
    {
        foreach (var name in names)
        {
            if (AgentJson.Has(body, name))
                errors.Add($"{Field(prefix, name)} is read-only.");
        }
    }

    private static bool TryAssignOptional(JsonElement body, string name, int max, List<string> errors, Action<string?> assign, string? prefix = null)
    {
        if (!TryReadString(body, name, max, errors, out var present, out var value, prefix))
            return false;
        if (!present)
            return false;
        assign(value);
        return true;
    }

    private static bool TryAssignEmail(JsonElement body, string name, int max, List<string> errors, Action<string?> assign, string? prefix = null)
    {
        if (!TryReadString(body, name, max, errors, out var present, out var value, prefix))
            return false;
        if (!present)
            return false;
        if (value != null && !new EmailAddressAttribute().IsValid(value))
        {
            errors.Add($"{Field(prefix, name)} is not a valid email address.");
            return false;
        }
        assign(value);
        return true;
    }

    private static bool TryAssignPhone(JsonElement body, string name, List<string> errors, Action<string?> assign, string? prefix = null)
    {
        if (!TryReadString(body, name, 50, errors, out var present, out var value, prefix))
            return false;
        if (!present)
            return false;
        if (value != null && !new PhoneAttribute().IsValid(value))
        {
            errors.Add($"{Field(prefix, name)} is not a valid phone number.");
            return false;
        }
        assign(value);
        return true;
    }

    private static bool TryReadString(
        JsonElement body,
        string name,
        int max,
        List<string> errors,
        out bool present,
        out string? value,
        string? prefix = null)
    {
        present = false;
        value = null;
        if (!AgentJson.Try(body, name, out var prop))
            return true;

        present = true;
        var label = Field(prefix, name);
        if (prop.ValueKind is JsonValueKind.Null)
            return true;
        if (prop.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{label} must be a string or null.");
            return false;
        }

        var text = prop.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
            return true;
        if (text.Length > max)
        {
            errors.Add($"{label} must be {max} characters or fewer.");
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryReadBool(
        JsonElement body,
        string name,
        List<string> errors,
        out bool present,
        out bool value,
        string? prefix = null)
    {
        present = false;
        value = false;
        if (!AgentJson.Try(body, name, out var prop))
            return true;

        present = true;
        if (prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = prop.GetBoolean();
            return true;
        }

        errors.Add($"{Field(prefix, name)} must be true or false.");
        return false;
    }

    private static bool TryParseCustomerType(string text, out CustomerType type, out string? error)
    {
        var compact = text.Replace(" ", "", StringComparison.Ordinal);
        if (compact.Equals("ExpandedProspect", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("Expanded", StringComparison.OrdinalIgnoreCase))
        {
            type = default;
            error = "type ExpandedProspect cannot be assigned through this API. Use Customer or Prospect.";
            return false;
        }

        if (TryParseEnum<CustomerType>(compact, out type) && type is CustomerType.Customer or CustomerType.Prospect)
        {
            error = null;
            return true;
        }

        type = default;
        error = "type must be Customer or Prospect.";
        return false;
    }

    private static bool TryParseEnum<TEnum>(string text, out TEnum value) where TEnum : struct, Enum
    {
        var compact = text.Replace(" ", "", StringComparison.Ordinal);
        return Enum.TryParse(compact, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    private static bool TryApplyTypeFilter(IQueryable<Customer> query, string? type, out IQueryable<Customer> filtered, out string? error)
    {
        filtered = query;
        error = null;
        if (string.IsNullOrWhiteSpace(type) || type.Equals("all", StringComparison.OrdinalIgnoreCase))
            return true;

        var compact = type.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (compact.Equals("customer", StringComparison.OrdinalIgnoreCase) || compact.Equals("customers", StringComparison.OrdinalIgnoreCase))
        {
            filtered = query.Where(c => c.Type == CustomerType.Customer);
            return true;
        }

        if (compact.Equals("prospect", StringComparison.OrdinalIgnoreCase) || compact.Equals("prospects", StringComparison.OrdinalIgnoreCase))
        {
            filtered = query.Where(c => c.Type == CustomerType.Prospect || c.Type == CustomerType.ExpandedProspect);
            return true;
        }

        if (compact.Equals("expandedprospect", StringComparison.OrdinalIgnoreCase) || compact.Equals("expanded", StringComparison.OrdinalIgnoreCase))
        {
            filtered = query.Where(c => c.Type == CustomerType.ExpandedProspect);
            return true;
        }

        error = "type must be Customer, Prospect, ExpandedProspect, or all.";
        return false;
    }

    private static bool TryApplyStatusFilter(IQueryable<Customer> query, string? status, out IQueryable<Customer> filtered, out string? error)
    {
        filtered = query;
        error = null;
        if (string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase))
            return true;

        if (TryParseEnum<CustomerStatus>(status.Trim(), out var parsed))
        {
            filtered = query.Where(c => c.Status == parsed);
            return true;
        }

        error = "status must be Active, Inactive, or all.";
        return false;
    }

    private static string Field(string? prefix, string name) =>
        string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
}

internal static class AgentJson
{
    public static bool Has(JsonElement body, string name) => Try(body, name, out _);

    public static bool Try(JsonElement body, string name, out JsonElement value)
    {
        if (body.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in body.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
