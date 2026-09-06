using System.Security.Claims;
using Backend.Controllers.Auth.Configuration;
using Backend.Controllers.Auth.Contracts;
using Backend.Controllers.Auth.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Controllers.Auth;

/// <summary>
/// Single-user session: password sign-in, sign-out and the current identity.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly ILoginAttemptLimiter _limiter;
    private readonly AuthOptions _options;

    public AuthController(ILoginAttemptLimiter limiter, IOptions<AuthOptions> options)
    {
        _limiter = limiter;
        _options = options.Value;
    }

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
        if (_limiter.IsBlocked(client))
        {
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new { error = "Too many failed attempts. Wait a minute and try again." });
        }

        if (!string.Equals(request.Password, _options.Password, StringComparison.Ordinal))
        {
            _limiter.RecordFailure(client);

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
