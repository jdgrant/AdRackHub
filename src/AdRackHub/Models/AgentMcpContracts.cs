namespace AdRackHub.Models;

public class AgentCreateToolResult
{
    public bool Created { get; set; }
    public string Message { get; set; } = string.Empty;
    public AgentCustomerDto? Customer { get; set; }
    public List<AgentCustomerDto> Duplicates { get; set; } = new();
}
