using Backend.Controllers.Auth.Configuration;
using Backend.Controllers.Features.Contracts;
using Backend.Infrastructure.Features;
using Backend.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Controllers.Features;

/// <summary>
/// What the running instance currently allows.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class FeaturesController : ControllerBase
{
    private readonly IOptionsSnapshot<FeatureOptions> _features;
    private readonly AuthOptions _auth;
    private readonly IAssetLinkSigner? _signer;

    // The signer is only in the container when the storage provider is a bucket, so the
    // parameter needs a default - without one the container would refuse to build this
    // controller at all on a local-storage instance.
    public FeaturesController(
        IOptionsSnapshot<FeatureOptions> features,
        IOptions<AuthOptions> auth,
        IAssetLinkSigner? signer = null)
    {
        _features = features;
        _auth = auth.Value;
        _signer = signer;
    }

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
            _features.Value.GoogleSignInEnabled && _auth.Google.IsConfigured,

            // Effective state again: the flag can be flipped at runtime, and on a local-storage
            // instance there is nothing to sign with.
            _features.Value.DirectAssetAccessEnabled && _signer is not null);
}
