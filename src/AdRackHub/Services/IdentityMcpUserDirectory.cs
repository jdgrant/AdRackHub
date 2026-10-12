using AdRackHub.Models;
using Microsoft.AspNetCore.Identity;

namespace AdRackHub.Services;

public sealed class IdentityMcpUserDirectory : IMcpUserDirectory
{
    private readonly UserManager<ApplicationUser> _users;

    public IdentityMcpUserDirectory(UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    public async Task<McpDirectoryUser?> FindAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null || string.IsNullOrWhiteSpace(user.Email))
            return null;

        var isAdmin = await _users.IsInRoleAsync(user, AppRoles.Admin);
        return new McpDirectoryUser(user.Id, user.Email, isAdmin, isAdmin);
    }
}
