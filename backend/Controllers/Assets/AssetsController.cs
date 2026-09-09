using Backend.Controllers.Assets.Contracts;
using Backend.Infrastructure.Features;
using Backend.Infrastructure.Persistence;
using Backend.Infrastructure.RealTime;
using Backend.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Controllers.Assets;

/// <summary>
/// Uploaded files: the raw PDFs and images notes will be built from.
/// </summary>
/// <remarks>
/// The table is the source of truth: the listing comes from it, and the storage only holds
/// bytes. Bytes without a row are invisible to the API and get collected separately.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
// IOptionsSnapshot, not IOptions: it is re-read per request, so a flag flipped in
// configuration takes effect without a restart.
//
// The signer is optional for the same reason it is in FeaturesController: local storage
// registers none, and a required parameter would break every action, not just signing.
public sealed class AssetsController(
    IAssetStorage storage,
    KnowledgeBaseDbContext database,
    IChangeNotifier notifier,
    IOptions<StorageOptions> options,
    IOptionsSnapshot<FeatureOptions> features,
    IAssetLinkSigner? signer = null)
    : ControllerBase
{
    private readonly IAssetStorage _storage = storage;
    private readonly KnowledgeBaseDbContext _database = database;
    private readonly IChangeNotifier _notifier = notifier;
    private readonly StorageOptions _options = options.Value;
    private readonly FeatureOptions _features = features.Value;
    private readonly IAssetLinkSigner? _signer = signer;

    /// <summary>
    /// Stores an uploaded file and returns its metadata.
    /// </summary>
    /// <param name="file">The PDF or image to store.</param>
    /// <param name="cancellationToken">Cancels the copy if the client disconnects.</param>
    /// <returns>Metadata of the stored file.</returns>
    /// <remarks>
    /// A stub: the file is kept as is. No AI processing, no .md generation, no indexing yet.
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

        // Bytes first, row second: a crash in between leaves an unreferenced file, which is
        // waste. The other order would leave a row the storage cannot answer for.
        var stored = await _storage.SaveAsync(content, file.FileName, cancellationToken);

        var record = AssetRecord.For(stored, file.FileName, file.ContentType, file.Length);

        _database.Assets.Add(record);

        // Deliberately not the request token: the bytes are already on disk, and a client that
        // walked away mid-request must not cost us the row that makes them findable.
        await _database.SaveChangesAsync(CancellationToken.None);

        var response = new UploadedAssetResponse(
            record.Id,
            record.StoredFileName,
            record.OriginalFileName,
            record.ContentType,
            record.SizeBytes,
            record.UploadedAtUtc);

        _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Created, stored.FileName));

        return CreatedAtAction(nameof(Download), new { fileName = stored.FileName }, response);
    }

    /// <summary>
    /// Lists every stored file, newest first.
    /// </summary>
    /// <param name="cancellationToken">Cancels the listing if the client disconnects.</param>
    /// <returns>Stored name, original name, size and upload time of each file.</returns>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AssetSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var assets = await _database.Assets
            .OrderByDescending(asset => asset.UploadedAtUtc)
            .Select(asset => new AssetSummaryResponse(
                asset.StoredFileName,
                asset.OriginalFileName,
                asset.SizeBytes,
                asset.UploadedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(assets);
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

        var record = await FindAsync(fileName, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        var content = await _storage.OpenReadAsync(record.StoredFileName, cancellationToken);

        if (content is null)
        {
            return NotFound();
        }

        // The browser saves it under the name the user picked, not under the GUID it is
        // stored as.
        return File(content, record.ContentType, record.OriginalFileName);
    }

    /// <summary>
    /// Returns a short-lived URL the browser fetches the bytes from directly.
    /// </summary>
    /// <param name="fileName">The stored name, as returned by the listing.</param>
    /// <param name="cancellationToken">Cancels the lookup if the client disconnects.</param>
    /// <returns>The signed URL and the moment it stops working.</returns>
    /// <remarks>
    /// The API never sees these bytes. The original name and content type live in the table,
    /// so they are signed into the link as response header overrides - that is what makes the
    /// browser save the file under the name the user picked rather than the GUID it is stored
    /// under. Issued per click, not per listing: the URL is a credential with a life of minutes.
    /// The object is checked for before signing, so a link is never handed out for bytes that
    /// are not there.
    /// </remarks>
    /// <response code="200">The signed URL.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="403">Downloading, or direct access, is switched off by a flag.</response>
    /// <response code="404">No such file.</response>
    /// <response code="503">This instance stores bytes locally and cannot sign anything.</response>
    [HttpGet("{fileName}/link")]
    [ProducesResponseType(typeof(AssetLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadLink(string fileName, CancellationToken cancellationToken)
    {
        if (!_features.DownloadEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Downloading is turned off." });
        }

        var refusal = RefuseWhenCannotSign();

        if (refusal is not null)
        {
            return refusal;
        }

        var record = await FindAsync(fileName, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        // Costs a HEAD per click, and buys the caller the same answer the streaming endpoint
        // gives: a row whose object is gone is a 404 here, not a link that hands the browser
        // an XML error page from the bucket.
        if (await _storage.GetAsync(record.StoredFileName, cancellationToken) is null)
        {
            return NotFound();
        }

        var link = _signer!.SignDownload(record.StoredFileName, record.OriginalFileName, record.ContentType);

        return Ok(new AssetLinkResponse(link.Url, link.ExpiresAtUtc));
    }

    /// <summary>
    /// Reserves a key in the bucket and returns a URL the browser PUTs the bytes to.
    /// </summary>
    /// <param name="request">Name, type and size of the file about to be sent.</param>
    /// <returns>The key, the signed URL, and the content type the PUT must carry.</returns>
    /// <remarks>
    /// Step one of two: nothing is stored yet and no row exists. The size here is what the
    /// client claims - a signed PUT cannot be capped, so the real check happens in Confirm,
    /// once the object is in the bucket and its size can be read rather than believed.
    /// </remarks>
    /// <response code="200">The signed URL.</response>
    /// <response code="400">The file is empty or over the size limit.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="403">Uploading, or direct access, is switched off by a flag.</response>
    /// <response code="503">This instance stores bytes locally and cannot sign anything.</response>
    [HttpPost("upload-link")]
    [ProducesResponseType(typeof(UploadLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult UploadLink([FromBody] UploadLinkRequest request)
    {
        if (!_features.UploadEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Uploading is turned off." });
        }

        var refusal = RefuseWhenCannotSign();

        if (refusal is not null)
        {
            return refusal;
        }

        if (request.SizeBytes <= 0)
        {
            return BadRequest(new { error = "File is empty." });
        }

        if (request.SizeBytes > _options.MaxUploadBytes)
        {
            return BadRequest(new { error = $"File exceeds the {_options.MaxUploadBytes / (1024 * 1024)} MB limit." });
        }

        var upload = _signer!.SignUpload(request.FileName, request.ContentType);

        return Ok(new UploadLinkResponse(upload.FileName, upload.Url, upload.ContentType, upload.ExpiresAtUtc));
    }

    /// <summary>
    /// Records an object the browser has already put in the bucket.
    /// </summary>
    /// <param name="fileName">The key handed out by upload-link.</param>
    /// <param name="request">The original name and type to store alongside it.</param>
    /// <param name="cancellationToken">Cancels the lookup if the client disconnects.</param>
    /// <returns>Metadata of the stored file.</returns>
    /// <remarks>
    /// Step two of two, and the only step that can create the row - a bucket cannot write to
    /// Postgres. The size is read back from the object rather than taken from the client,
    /// because the signed PUT accepted whatever was actually sent.
    /// An object that never gets confirmed stays invisible to the API and is collected
    /// separately; that is the price of the bytes not passing through here.
    /// </remarks>
    /// <response code="201">The row exists; the file is now in the listing.</response>
    /// <response code="400">The object is empty or over the size limit; it has been removed.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="403">Uploading, or direct access, is switched off by a flag.</response>
    /// <response code="404">No such object in the bucket.</response>
    /// <response code="409">This key was already confirmed.</response>
    [HttpPost("{fileName}/confirm")]
    [ProducesResponseType(typeof(UploadedAssetResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmUpload(
        string fileName,
        [FromBody] ConfirmUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (!_features.UploadEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Uploading is turned off." });
        }

        if (!_features.DirectAssetAccessEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Direct bucket access is turned off." });
        }

        // A retried confirm - the answer got lost, the user pressed the button again - must not
        // leave two rows pointing at one object.
        if (await FindAsync(fileName, cancellationToken) is not null)
        {
            return Conflict(new { error = "This file has already been confirmed." });
        }

        var stored = await _storage.GetAsync(fileName, cancellationToken);

        if (stored is null)
        {
            return NotFound(new { error = "No such object in the bucket." });
        }

        if (stored.SizeBytes <= 0 || stored.SizeBytes > _options.MaxUploadBytes)
        {
            // Refusing the row is not enough: the bytes are already there, and without a row
            // nothing would ever look at them again.
            await _storage.DeleteAsync(fileName, CancellationToken.None);

            return BadRequest(new
            {
                error = stored.SizeBytes <= 0
                    ? "File is empty."
                    : $"File exceeds the {_options.MaxUploadBytes / (1024 * 1024)} MB limit.",
            });
        }

        var record = AssetRecord.For(
            new StoredAsset(AssetFileName.IdOf(fileName), fileName),
            request.OriginalFileName,
            request.ContentType,
            stored.SizeBytes);

        _database.Assets.Add(record);

        await _database.SaveChangesAsync(CancellationToken.None);

        var response = new UploadedAssetResponse(
            record.Id,
            record.StoredFileName,
            record.OriginalFileName,
            record.ContentType,
            record.SizeBytes,
            record.UploadedAtUtc);

        _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Created, fileName));

        return CreatedAtAction(nameof(Download), new { fileName }, response);
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
        var record = await FindAsync(fileName, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        // Row first: the file leaves the listing even if the storage call below fails, and a
        // leftover object is easier to live with than a row pointing at nothing.
        _database.Assets.Remove(record);
        await _database.SaveChangesAsync(CancellationToken.None);

        await _storage.DeleteAsync(record.StoredFileName, cancellationToken);

        _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Deleted, fileName));

        return NoContent();
    }

    private Task<AssetRecord?> FindAsync(string fileName, CancellationToken cancellationToken) =>
        _database.Assets.FirstOrDefaultAsync(asset => asset.StoredFileName == fileName, cancellationToken);

    // Two separate reasons the browser cannot be sent to the bucket, and they deserve
    // different statuses: the flag is a decision, a missing signer is a misconfiguration.
    private IActionResult? RefuseWhenCannotSign()
    {
        if (!_features.DirectAssetAccessEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Direct bucket access is turned off." });
        }

        return _signer is null
            ? StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { error = "This instance stores files locally and cannot sign bucket links." })
            : null;
    }
}
