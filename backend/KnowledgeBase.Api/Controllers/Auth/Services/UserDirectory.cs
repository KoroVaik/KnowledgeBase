using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.Auth.Services;

public sealed class UserDirectory(KnowledgeBaseDbContext database) : IUserDirectory
{
    private readonly KnowledgeBaseDbContext _database = database;

    public async Task<string> ResolveGoogleUserIdAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();

        var userId = await _database.Users
            .Where(user => user.Email == normalized)
            .Select(user => user.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // The allow-list predates accounts and names the owner's own addresses.
        return userId ?? UserAccount.OwnerId;
    }
}
