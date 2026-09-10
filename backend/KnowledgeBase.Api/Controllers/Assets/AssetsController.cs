using KnowledgeBase.Api.Controllers.Assets.Contracts;
using KnowledgeBase.Api.Infrastructure.Features;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Controllers.Assets;

/// <summary>
/// Uploaded files: the raw PDFs and images notes will be built from.
/// </summary>
/// <remarks>
/// The table is the source of truth: the listing comes from it, and the storage only holds
/// bytes. Bytes without a row are invisible to the API and get collected separately.
///
/// The bytes themselves never pass through here - the browser PUTs them to the bucket and
/// GETs them back over signed URLs. This controller only signs, lists and deletes.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
// IOptionsSnapshot, not IOptions: it is re-read per request, so a flag flipped in
// configuration takes effect without a restart.
public sealed class AssetsController(
    IAssetStorage storage,
    KnowledgeBaseDbContext database,
    IChangeNotifier notifier,
    IOptions<StorageOptions> options,
    IOptionsSnapshot<FeatureOptions> features,
    IAssetLinkSigner signer)
    : ControllerBase
{
    private readonly IAssetStorage _storage = storage;
    private readonly KnowledgeBaseDbContext _database = database;
    private readonly IChangeNotifier _notifier = notifier;
    private readonly StorageOptions _options = options.Value;
    private readonly FeatureOptions _features = features.Value;
    private readonly IAssetLinkSigner _signer = signer;

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
        // One row per asset plus, where they exist, its pipeline job and the note it produced.
        // Left joins, because most of the interesting states are "no job yet" or "no note yet";
        // GroupJoin + SelectMany with DefaultIfEmpty is how EF spells LEFT JOIN.
        var rows = await _database.Assets
            .GroupJoin(
                _database.ProcessingJobs,
                asset => asset.Id,
                job => job.AssetId,
                (asset, jobs) => new { asset, jobs })
            .SelectMany(
                pair => pair.jobs.DefaultIfEmpty(),
                (pair, job) => new { pair.asset, job })
            .GroupJoin(
                _database.Notes,
                pair => pair.asset.Id,
                note => note.SourceAssetId,
                (pair, notes) => new { pair.asset, pair.job, notes })
            .SelectMany(
                group => group.notes.DefaultIfEmpty(),
                (group, note) => new { group.asset, group.job, note })
            .OrderByDescending(row => row.asset.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        var assets = rows
            .Select(row => new AssetSummaryResponse(
                row.asset.StoredFileName,
                row.asset.OriginalFileName,
                row.asset.SizeBytes,
                row.asset.UploadedAtUtc,
                row.job?.Status.ToString(),
                row.job?.Error,
                row.note?.Id))
            .ToList();

        return Ok(assets);
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
    /// <response code="403">Downloading is switched off by the Features:DownloadEnabled flag.</response>
    /// <response code="404">No such file.</response>
    [HttpGet("{fileName}/link")]
    [ProducesResponseType(typeof(AssetLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadLink(string fileName, CancellationToken cancellationToken)
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

        // Costs a HEAD per click: a row whose object is gone is a 404 here, not a link that
        // hands the browser an XML error page from the bucket.
        if (await _storage.GetAsync(record.StoredFileName, cancellationToken) is null)
        {
            return NotFound();
        }

        var link = _signer.SignDownload(record.StoredFileName, record.OriginalFileName, record.ContentType);

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
    /// <response code="403">Uploading is switched off by the Features:UploadEnabled flag.</response>
    [HttpPost("upload-link")]
    [ProducesResponseType(typeof(UploadLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult UploadLink([FromBody] UploadLinkRequest request)
    {
        if (!_features.UploadEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Uploading is turned off." });
        }

        if (request.SizeBytes <= 0)
        {
            return BadRequest(new { error = "File is empty." });
        }

        if (request.SizeBytes > _options.MaxUploadBytes)
        {
            return BadRequest(new { error = $"File exceeds the {_options.MaxUploadBytes / (1024 * 1024)} MB limit." });
        }

        var upload = _signer.SignUpload(request.FileName, request.ContentType);

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
    /// <response code="403">Uploading is switched off by the Features:UploadEnabled flag.</response>
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

        // Same SaveChanges, so the job and its asset commit together: no queued row without a
        // file, no file the pipeline never looks at.
        if (ProcessableContent.Classify(record.ContentType, record.OriginalFileName) is not null)
        {
            _database.ProcessingJobs.Add(ProcessingJob.Queue(record.Id));
        }

        await _database.SaveChangesAsync(CancellationToken.None);

        var response = new UploadedAssetResponse(
            record.Id,
            record.StoredFileName,
            record.OriginalFileName,
            record.ContentType,
            record.SizeBytes,
            record.UploadedAtUtc);

        _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Created, fileName));

        return CreatedAtAction(nameof(DownloadLink), new { fileName }, response);
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

        // The note is a structured description of this file, so it goes with it - into the bin,
        // not out of existence, so links pointing at it can say "deleted" rather than go blank.
        var note = await _database.Notes
            .FirstOrDefaultAsync(note => note.SourceAssetId == record.Id, cancellationToken);

        if (note is not null)
        {
            note.DeletedAtUtc = DateTime.UtcNow;
        }

        // Row first: the file leaves the listing even if the storage call below fails, and a
        // leftover object is easier to live with than a row pointing at nothing.
        _database.Assets.Remove(record);
        await _database.SaveChangesAsync(CancellationToken.None);

        await _storage.DeleteAsync(record.StoredFileName, cancellationToken);

        _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Deleted, fileName));

        if (note is not null)
        {
            _notifier.Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Deleted, note.Id));
        }

        return NoContent();
    }

    /// <summary>
    /// Puts the file back in front of the worker.
    /// </summary>
    /// <param name="fileName">The stored name, as returned by the listing.</param>
    /// <param name="cancellationToken">Cancels the call if the client disconnects.</param>
    /// <remarks>
    /// For files the pipeline failed on, skipped, or whose note has been binned. Redoing a file
    /// that still has a note is the same call under a different name on the note itself.
    /// </remarks>
    /// <response code="202">Queued.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such file.</response>
    /// <response code="409">A run is already queued.</response>
    [HttpPost("{fileName}/process")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Process(string fileName, CancellationToken cancellationToken)
    {
        var record = await FindAsync(fileName, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        if (ProcessableContent.Classify(record.ContentType, record.OriginalFileName) is null)
        {
            return Conflict(new { error = "The pipeline does not handle this kind of file." });
        }

        if (!await ProcessingQueue.EnsurePendingAsync(_database, record.Id, cancellationToken))
        {
            return Conflict(new { error = "This file is already queued." });
        }

        await _database.SaveChangesAsync(CancellationToken.None);

        _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Updated, fileName));

        return Accepted();
    }

    private Task<AssetRecord?> FindAsync(string fileName, CancellationToken cancellationToken) =>
        _database.Assets.FirstOrDefaultAsync(asset => asset.StoredFileName == fileName, cancellationToken);
}
