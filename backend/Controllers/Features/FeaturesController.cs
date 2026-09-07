using Backend.Controllers.Features.Contracts;
using Backend.Infrastructure.Features;
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

    public FeaturesController(IOptionsSnapshot<FeatureOptions> features)
    {
        _features = features;
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
        new(_features.Value.UploadEnabled, _features.Value.DownloadEnabled);
}
