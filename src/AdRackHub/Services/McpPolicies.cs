namespace AdRackHub.Services;

public static class McpPolicies
{
    public const string Read = "McpRead";
    public const string Write = "McpWrite";
    public const string ScopeClaim = "mcp_scope";
    public const string ReadScope = "customers:read";
    public const string WriteScope = "customers:write";
}
