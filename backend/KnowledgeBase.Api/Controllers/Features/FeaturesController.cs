using KnowledgeBase.Api.Controllers.Auth.Configuration;
using KnowledgeBase.Api.Controllers.Features.Contracts;
using KnowledgeBase.Api.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Controllers.Features;

/// <summary>
/// What the running instance currently allows.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class FeaturesController(
    IOptionsSnapshot<FeatureOptions> features,
    IOptions<AuthOptions> auth)
    : ControllerBase
{
    private readonly IOptionsSnapshot<FeatureOptions> _features = features;
    private readonly AuthOptions _auth = auth.Value;

    /// <summary>
    /// Returns the feature flags the UI has to respect.
    /// </summary>
    /// <returns>The current flags.</returns>
    /// <remarks>
    /// Anonymous on purpose: the sign-in screen needs the flags before there is a session.
    /// The flags shape the UI only - every switched-off feature is refused server-side too.
    /// </remarks>
    /// <response code="200">The flags.</response>
    [HttpGet]
    [ProducesResponseType(typeof(FeatureFlagsResponse), StatusCodes.Status200OK)]
    public FeatureFlagsResponse Get() =>
        new(
            _features.Value.UploadEnabled,
            _features.Value.DownloadEnabled,
            // Reported as the effective state, not the raw flag: the Google scheme is registered
            // only when the credentials are there, so a flag switched on without them would put
            // a button on screen that can only answer 404.
            _features.Value.GoogleSignInEnabled && _auth.Google.IsConfigured);
}
