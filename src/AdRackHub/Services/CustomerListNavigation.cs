using AdRackHub.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AdRackHub.Services;

public class CustomerListNavState
{
    public CustomerType Type { get; set; }
    public string? Search { get; set; }
    public CustomerStatus? Status { get; set; }
    public List<int> Ids { get; set; } = new();
}

public static class CustomerListNavigation
{
    public const string SessionKey = "CustomerListNav";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IQueryable<Customer> ApplyListFilters(
        IQueryable<Customer> query,
        CustomerType type,
        string? search,
        CustomerStatus? status)
    {
        query = query.Where(c => c.Type == type);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c =>
                c.CustomerName.Contains(search) ||
                c.Contacts.Any(ct =>
                    ct.Name.Contains(search)
                    || (ct.FirstName != null && ct.FirstName.Contains(search))
                    || (ct.LastName != null && ct.LastName.Contains(search))
                    || (ct.Email != null && ct.Email.Contains(search))));
        }

        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);

        return query.OrderBy(c => c.CustomerName);
    }

    public static async Task<List<int>> GetOrderedIdsAsync(
        DbContext context,
        CustomerType type,
        string? search,
        CustomerStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyListFilters(context.Set<Customer>().AsQueryable(), type, search, status);
        return await query.Select(c => c.Id).ToListAsync(cancellationToken);
    }

    public static void Store(ISession session, CustomerListNavState state)
    {
        session.SetString(SessionKey, JsonSerializer.Serialize(state, JsonOptions));
    }

    public static CustomerListNavState? Load(ISession session)
    {
        var json = session.GetString(SessionKey);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<CustomerListNavState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static (int? PreviousId, int? NextId, int Position, int Total) ResolveNeighbors(IReadOnlyList<int> ids, int currentId)
    {
        if (ids.Count == 0)
            return (null, null, 0, 0);

        var index = -1;
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == currentId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return (null, null, 0, ids.Count);

        var previousId = index > 0 ? ids[index - 1] : (int?)null;
        var nextId = index < ids.Count - 1 ? ids[index + 1] : (int?)null;
        return (previousId, nextId, index + 1, ids.Count);
    }
}
