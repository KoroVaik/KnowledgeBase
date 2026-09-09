using System.Security.Claims;
using KnowledgeBase.Api.Controllers.Auth.Configuration;
using KnowledgeBase.Api.Controllers.Auth.Contracts;
using KnowledgeBase.Api.Controllers.Auth.Services;
using KnowledgeBase.Api.Features;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Controllers.Auth;

/// <summary>
/// Single-user session: password sign-in, sign-out and the current identity.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class AuthController(
    ILoginAttemptLimiter limiter,
    IOptions<AuthOptions> options,
    IOptionsSnapshot<FeatureOptions> features)
    : ControllerBase
{
    private readonly AuthOptions _options = options.Value;

    /// <summary>
    /// Signs the owner in and issues the session cookie.
    /// </summary>
    /// <param name="request">The owner password.</param>
    /// <returns>The signed-in user.</returns>
    /// <response code="200">Signed in; the session cookie is set.</response>
    /// <response code="401">Wrong password.</response>
    /// <response code="429">Too many failed attempts from this address.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var client = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Checked before the password is verified, so a client that has run out of attempts gets
        // no guessing feedback at all.
        if (limiter.IsBlocked(client))
        {
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new { error = "Too many failed attempts. Wait a minute and try again." });
        }

        if (!string.Equals(request.Password, _options.Password, StringComparison.Ordinal))
        {
            limiter.RecordFailure(client);

            return Unauthorized(new { error = "Invalid password." });
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, _options.OwnerName)],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return Ok(new CurrentUserResponse(_options.OwnerName));
    }

    /// <summary>
    /// Sends the browser to Google's consent screen.
    /// </summary>
    /// <remarks>
    /// A redirect, not JSON: an OAuth flow is a chain of top-level navigations, and fetch()
    /// cannot carry the user through Google's own pages. Google returns to the callback path,
    /// where the handler issues the same session cookie the password login does.
    /// </remarks>
    /// <response code="302">Redirecting to Google.</response>
    /// <response code="404">Google sign-in is switched off or has no credentials.</response>
    [HttpGet("google/start")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult StartGoogleSignIn()
    {
        // Checked here too, not only in the UI: a flag that merely hides a button is decoration.
        if (!features.Value.GoogleSignInEnabled || !_options.Google.IsConfigured)
        {
            return NotFound();
        }

        return Challenge(
            new AuthenticationProperties { RedirectUri = "/" },
            GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Clears the session cookie.
    /// </summary>
    /// <response code="204">Signed out.</response>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return NoContent();
    }

    /// <summary>
    /// Returns the user behind the current session cookie.
    /// </summary>
    /// <returns>The signed-in user.</returns>
    /// <response code="200">The session is valid.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<CurrentUserResponse> Me() =>
        Ok(new CurrentUserResponse(User.Identity?.Name ?? _options.OwnerName));
}
