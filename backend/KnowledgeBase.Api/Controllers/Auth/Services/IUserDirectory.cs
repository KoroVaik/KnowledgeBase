namespace KnowledgeBase.Api.Controllers.Auth.Services;

public interface IUserDirectory
{
    /// <summary>The account an allow-listed Google email signs in as.</summary>
    Task<string> ResolveGoogleUserIdAsync(string email, CancellationToken cancellationToken);
}
