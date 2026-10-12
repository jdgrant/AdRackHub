namespace AdRackHub.Services;

public interface IMcpUserDirectory
{
    Task<McpDirectoryUser?> FindAsync(string userId, CancellationToken cancellationToken);
}

public sealed record McpDirectoryUser(string Id, string Email, bool IsAdmin, bool EmailVerified);
