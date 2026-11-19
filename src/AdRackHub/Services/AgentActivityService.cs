using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public interface IAgentActivityApi
{
    Task<AgentCallResult<AgentActivityListDto>> ListAsync(
        string? status,
        string? kind,
        int? customerId,
        string? search,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken);

    Task<AgentCallResult<AgentActivityDto>> CreateAsync(
        int? customerId,
        string? customerName,
        string? kind,
        string? status,
        string? dueDate,
        string? body,
        string? createdBy,
        string hubBase,
        CancellationToken cancellationToken);
}

public class AgentActivityService : IAgentActivityApi
{
    private readonly ApplicationDbContext _context;

    public AgentActivityService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AgentCallResult<AgentActivityListDto>> ListAsync(
        string? status,
        string? kind,
        int? customerId,
        string? search,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken)
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = AgentCustomerApiService.DefaultPageSize;
        if (pageSize > AgentCustomerApiService.MaxPageSize)
            pageSize = AgentCustomerApiService.MaxPageSize;

        var query = _context.CustomerNotes.AsNoTracking().Include(n => n.Customer).AsQueryable();

        var statusFilter = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim();
        if (statusFilter.Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(n => n.Status == CustomerNoteStatus.New || n.Status == CustomerNoteStatus.Working);
        }
        else if (!statusFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<CustomerNoteStatus>(statusFilter, true, out var parsedStatus))
                return AgentCallResult<AgentActivityListDto>.Invalid(["status must be open, all, New, Working, or Done."]);
            query = query.Where(n => n.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(kind) && !kind.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseKind(kind, out var parsedKind))
                return AgentCallResult<AgentActivityListDto>.Invalid(["kind must be Note, Call, Email, Meeting, Action, Task, Brochures Needed, or all."]);
            query = query.Where(n => n.Kind == parsedKind);
        }

        if (customerId is int id)
            query = query.Where(n => n.CustomerId == id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(n =>
                n.Body.ToLower().Contains(term)
                || n.Customer.CustomerName.ToLower().Contains(term)
                || (n.CreatedBy != null && n.CreatedBy.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var notes = await query
            .OrderBy(n => n.DueDate == null)
            .ThenBy(n => n.DueDate)
            .ThenByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return AgentCallResult<AgentActivityListDto>.Ok(new AgentActivityListDto
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = totalPages,
            HasMore = page < totalPages,
            Items = notes.Select(note => ToDto(note, hubBase)).ToList()
        });
    }

    public async Task<AgentCallResult<AgentActivityDto>> CreateAsync(
        int? customerId,
        string? customerName,
        string? kind,
        string? status,
        string? dueDate,
        string? body,
        string? createdBy,
        string hubBase,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return AgentCallResult<AgentActivityDto>.Invalid(["body is required."]);
        var text = body.Trim();
        if (text.Length > 4000)
            return AgentCallResult<AgentActivityDto>.Invalid(["body cannot be longer than 4000 characters."]);

        var resolvedKind = CustomerNoteKind.Note;
        if (!string.IsNullOrWhiteSpace(kind) && !TryParseKind(kind, out resolvedKind))
            return AgentCallResult<AgentActivityDto>.Invalid(["kind must be Note, Call, Email, Meeting, Action, Task, or Brochures Needed."]);

        var resolvedStatus = CustomerNoteStatus.New;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<CustomerNoteStatus>(status.Trim(), true, out resolvedStatus))
                return AgentCallResult<AgentActivityDto>.Invalid(["status must be New, Working, or Done."]);
        }

        DateOnly? due = null;
        if (!string.IsNullOrWhiteSpace(dueDate))
        {
            if (!DateOnly.TryParse(dueDate.Trim(), out var parsedDue))
                return AgentCallResult<AgentActivityDto>.Invalid(["dueDate must be yyyy-MM-dd."]);
            due = parsedDue;
        }
        else if (resolvedKind is CustomerNoteKind.Task or CustomerNoteKind.BrochuresNeeded)
        {
            due = DateOnly.FromDateTime(DateTime.Today);
        }

        Customer? customer;
        if (customerId is int id)
        {
            customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (customer == null)
                return AgentCallResult<AgentActivityDto>.Missing("No customer or prospect has that id.");
        }
        else if (!string.IsNullOrWhiteSpace(customerName))
        {
            var name = customerName.Trim().ToLower();
            var matches = await _context.Customers
                .Where(c => c.CustomerName.ToLower() == name)
                .ToListAsync(cancellationToken);
            if (matches.Count == 0)
                return AgentCallResult<AgentActivityDto>.Missing("No customer or prospect has that name. Use search_customers and pass customerId.");
            if (matches.Count > 1)
                return AgentCallResult<AgentActivityDto>.Fail(409, "More than one record has that name. Use search_customers and pass customerId.");
            customer = matches[0];
        }
        else
        {
            return AgentCallResult<AgentActivityDto>.Invalid(["customerId or customerName is required."]);
        }

        var note = new CustomerNote
        {
            CustomerId = customer.Id,
            Kind = resolvedKind,
            Status = resolvedStatus,
            DueDate = due,
            Body = text,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? null : createdBy.Trim()
        };
        _context.CustomerNotes.Add(note);
        await _context.SaveChangesAsync(cancellationToken);
        note.Customer = customer;

        var hubUrl = $"{hubBase.TrimEnd('/')}/Customers/Details/{customer.Id}";
        return AgentCallResult<AgentActivityDto>.Created(ToDto(note, hubBase), hubUrl);
    }

    private static bool TryParseKind(string raw, out CustomerNoteKind kind)
    {
        var text = raw.Trim();
        if (Enum.TryParse(text, true, out kind) && Enum.IsDefined(kind))
            return true;

        var compact = new string(text.Where(char.IsLetterOrDigit).ToArray());
        if (compact.Equals("BrochuresNeeded", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("BrochuresNeeded911", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("911", StringComparison.Ordinal))
        {
            kind = CustomerNoteKind.BrochuresNeeded;
            return true;
        }

        kind = default;
        return false;
    }

    private static AgentActivityDto ToDto(CustomerNote note, string hubBase) => new()
    {
        Id = note.Id,
        CustomerId = note.CustomerId,
        CustomerName = note.Customer.CustomerName,
        CustomerType = note.Customer.Type.ToString(),
        Kind = note.Kind.ToString(),
        Status = note.Status.ToString(),
        DueDate = note.DueDate?.ToString("yyyy-MM-dd"),
        Body = note.Body,
        CreatedAt = note.CreatedAt.Kind == DateTimeKind.Local
            ? note.CreatedAt.ToUniversalTime().ToString("o")
            : DateTime.SpecifyKind(note.CreatedAt, DateTimeKind.Utc).ToString("o"),
        CreatedBy = note.CreatedBy,
        IsOverdue = note.IsOverdue,
        HubUrl = $"{hubBase.TrimEnd('/')}/Customers/Details/{note.CustomerId}"
    };
}
