using KnowledgeBase.Api.Controllers.Auth.Configuration;
using KnowledgeBase.Api.Controllers.Features.Contracts;
using KnowledgeBase.Api.Infrastructure.Features;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Controllers.Features;

/// <summary>What the running instance currently allows.</summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class FeaturesController(
    IOptionsSnapshot<FeatureOptions> features,
    IOptions<AuthOptions> auth,
    IOptions<PipelineOptions> pipeline,
    IOptions<StorageOptions> storage)
    : ControllerBase
{
    private readonly IOptionsSnapshot<FeatureOptions> _features = features;
    private readonly AuthOptions _auth = auth.Value;
    private readonly PipelineOptions _pipeline = pipeline.Value;
    private readonly StorageOptions _storage = storage.Value;

    /// <summary>
    /// Returns the feature flags the UI has to respect. Anonymous - the sign-in screen needs
    /// them before there is a session. Every switched-off feature is also refused server-side.
    /// </summary>
    /// <response code="200">The flags.</response>
    [HttpGet]
    [ProducesResponseType(typeof(FeatureFlagsResponse), StatusCodes.Status200OK)]
    public FeatureFlagsResponse Get() =>
        new(
            _features.Value.UploadEnabled,
            // Effective state: the Google scheme registers only with credentials, so a flag on
            // without them would show a button that can only answer 404.
            _features.Value.GoogleSignInEnabled && _auth.Google.IsConfigured,
            _pipeline.MaxSourceChars,
            _storage.MaxUploadBytes);
}
