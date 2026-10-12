using System.Text.Json;
using AdRackHub.Models;

namespace AdRackHub.Services;

public interface IAgentCustomerApi
{
    Task<AgentCallResult<AgentCustomerListDto>> ListAsync(
        string? type,
        string? status,
        string? search,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken);

    Task<AgentCallResult<AgentCustomerDto>> GetAsync(int id, string hubBase, CancellationToken cancellationToken);

    Task<AgentCallResult<AgentCustomerDto>> CreateAsync(JsonElement body, string hubBase, CancellationToken cancellationToken);

    Task<AgentCallResult<AgentCustomerDto>> UpdateAsync(int id, JsonElement body, string hubBase, CancellationToken cancellationToken);

    Task<AgentCallResult<AgentCustomerDto>> AddContactAsync(int customerId, JsonElement body, string hubBase, CancellationToken cancellationToken);

    Task<AgentCallResult<AgentCustomerDto>> UpdateContactAsync(
        int customerId,
        int contactId,
        JsonElement body,
        string hubBase,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentCustomerDto>> FindPotentialDuplicatesAsync(
        string? customerName,
        string? email,
        string? phone,
        string hubBase,
        CancellationToken cancellationToken);
}
