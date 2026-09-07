using Backend.Controllers.Assets.Contracts;
using Backend.Infrastructure.Features;
using Backend.Infrastructure.RealTime;
using Backend.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace Backend.Controllers.Assets;

/// <summary>
/// Uploaded files: the raw PDFs and images notes will be built from.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class AssetsController : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly IAssetStorage _storage;
    private readonly IChangeNotifier _notifier;
    private readonly StorageOptions _options;
    private readonly FeatureOptions _features;

    // IOptionsSnapshot, not IOptions: it is re-read per request, so a flag flipped in
    // configuration takes effect without a restart.
    public AssetsController(
        IAssetStorage storage,
        IChangeNotifier notifier,
        IOptions<StorageOptions> options,
        IOptionsSnapshot<FeatureOptions> features)
    {
        _storage = storage;
        _notifier = notifier;
        _options = options.Value;
        _features = features.Value;
    }

    /// <summary>
    /// Stores an uploaded file and returns its metadata.
    /// </summary>
    /// <param name="file">The PDF or image to store.</param>
    /// <param name="cancellationToken">Cancels the copy if the client disconnects.</param>
    /// <returns>Metadata of the stored file.</returns>
    /// <remarks>
    /// A stub: the file is kept as is. No AI processing, no .md generation, no indexing yet.
    /// The original name and content type are echoed back but not persisted anywhere.
    /// </remarks>
    /// <response code="201">The file is stored.</response>
    /// <response code="400">The file is empty or over the size limit.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="403">Uploading is switched off by the Features:UploadEnabled flag.</response>
    [HttpPost]
    [ProducesResponseType(typeof(UploadedAssetResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        // A disabled control in the UI is a hint, not a restriction: the refusal has to live here.
        if (!_features.UploadEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Uploading is turned off." });
        }

        if (file.Length == 0)
        {
            return BadRequest(new { error = "File is empty." });
        }

        if (file.Length > _options.MaxUploadBytes)
        {
            return BadRequest(new { error = $"File exceeds the {_options.MaxUploadBytes / (1024 * 1024)} MB limit." });
        }

        await using var content = file.OpenReadStream();
        var stored = await _storage.SaveAsync(content, file.FileName, cancellationToken);

        var response = new UploadedAssetResponse(
            stored.Id,
            stored.FileName,
            Path.GetFileName(file.FileName),
            file.ContentType,
            file.Length,
            DateTimeOffset.UtcNow);

        _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Created, stored.FileName));

        return CreatedAtAction(nameof(Download), new { fileName = stored.FileName }, response);
    }

    /// <summary>
    /// Lists every stored file, newest first.
    /// </summary>
    /// <param name="cancellationToken">Cancels the listing if the client disconnects.</param>
    /// <returns>Stored name, size and last modification time of each file.</returns>
    /// <remarks>
    /// Only what the storage itself knows: the original file name is not kept anywhere.
    /// </remarks>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AssetSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var assets = await _storage.ListAsync(cancellationToken);

        return Ok(assets
            .Select(asset => new AssetSummaryResponse(asset.FileName, asset.SizeBytes, asset.LastModifiedUtc))
            .ToList());
    }

    /// <summary>
    /// Returns the bytes of one stored file.
    /// </summary>
    /// <param name="fileName">The stored name, as returned by the listing.</param>
    /// <param name="cancellationToken">Cancels the transfer if the client disconnects.</param>
    /// <returns>The file itself.</returns>
    /// <remarks>
    /// The bytes travel through the API even when they live in object storage. Handing out a
    /// presigned URL instead is a separate step.
    /// </remarks>
    /// <response code="200">The file.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="403">Downloading is switched off by the Features:DownloadEnabled flag.</response>
    /// <response code="404">No such file.</response>
    [HttpGet("{fileName}")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string fileName, CancellationToken cancellationToken)
    {
        if (!_features.DownloadEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Downloading is turned off." });
        }

        var content = await _storage.OpenReadAsync(fileName, cancellationToken);

        if (content is null)
        {
            return NotFound();
        }

        // Nothing stores the content type, so it is guessed back from the extension the
        // upload kept.
        var contentType = ContentTypes.TryGetContentType(fileName, out var known)
            ? known
            : "application/octet-stream";

        return File(content, contentType, fileName);
    }

    /// <summary>
    /// Deletes one stored file.
    /// </summary>
    /// <param name="fileName">The stored name, as returned by the listing.</param>
    /// <param name="cancellationToken">Cancels the call if the client disconnects.</param>
    /// <response code="204">The file is gone.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such file.</response>
    [HttpDelete("{fileName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string fileName, CancellationToken cancellationToken)
    {
        if (!await _storage.DeleteAsync(fileName, cancellationToken))
        {
            return NotFound();
        }

        _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Deleted, fileName));

        return NoContent();
    }
}
