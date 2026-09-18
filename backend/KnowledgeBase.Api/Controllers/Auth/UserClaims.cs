using System.Security.Claims;

namespace KnowledgeBase.Api.Controllers.Auth;

public static class UserClaims
{
    // Not ClaimTypes.NameIdentifier: Google fills that with its own account id.
    public const string UserId = "kb:user-id";

    /// <summary>The <c>Users</c> row behind the session. The cookie validator rejects any
    /// session without it, so inside an [Authorize] action it is always present.</summary>
    public static string GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirst(UserId)?.Value
        ?? throw new InvalidOperationException("The session carries no user id.");
}
