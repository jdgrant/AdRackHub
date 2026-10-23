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
            if (!Enum.TryParse<CustomerNoteKind>(kind.Trim(), true, out var parsedKind))
                return AgentCallResult<AgentActivityListDto>.Invalid(["kind must be Note, Call, Email, Meeting, Action, Task, BrochuresNeeded, or all."]);
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
            Items = notes.Select(note => new AgentActivityDto
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
            }).ToList()
        });
    }
}
