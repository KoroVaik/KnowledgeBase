using KnowledgeBase.Api.Controllers.Assets.Contracts;
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
/// Uploaded files: the raw PDFs and images notes are built from. The table is the source of
/// truth; the bytes go browser ↔ bucket over signed URLs and never pass through here.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class AssetsController(
    IAssetStorage storage,
    KnowledgeBaseDbContext database,
    IChangeNotifier notifier,
    IOptions<StorageOptions> options,
    IAssetLinkSigner signer)
    : ControllerBase
{
    private readonly IAssetStorage _storage = storage;
    private readonly KnowledgeBaseDbContext _database = database;
    private readonly IChangeNotifier _notifier = notifier;
    private readonly StorageOptions _options = options.Value;
    private readonly IAssetLinkSigner _signer = signer;

    /// <summary>Lists every stored file, newest first, with its job state and note id.</summary>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AssetSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        // LEFT JOIN asset → its job → its note. GroupJoin + SelectMany + DefaultIfEmpty is
        // how EF spells LEFT JOIN.
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
                row.asset.ContentType,
                row.asset.SizeBytes,
                row.asset.UploadedAtUtc,
                row.job?.Status.ToString(),
                row.job?.Error,
                row.note?.Id))
            .ToList();

        return Ok(assets);
    }

    /// <summary>
    /// Returns a short-lived signed URL the browser fetches the bytes from directly. Name and
    /// content type are signed in as response-header overrides. Issued per click.
    /// </summary>
    /// <response code="200">The signed URL.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such file.</response>
    [HttpGet("{fileName}/link")]
    [ProducesResponseType(typeof(AssetLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadLink(string fileName, CancellationToken cancellationToken)
    {
        var record = await FindAsync(fileName, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        // A HEAD per click: a row whose object is gone is a 404, not a link to a bucket XML error.
        if (await _storage.GetAsync(record.StoredFileName, cancellationToken) is null)
        {
            return NotFound();
        }

        var link = _signer.SignDownload(record.StoredFileName, record.OriginalFileName, record.ContentType);

        return Ok(new AssetLinkResponse(link.Url, link.ExpiresAtUtc));
    }

    /// <summary>
    /// Step 1 of 2: reserves a key and returns a signed URL the browser PUTs the bytes to.
    /// The size here is the client's claim - the real check is in Confirm.
    /// </summary>
    /// <response code="200">The signed URL.</response>
    /// <response code="400">The file is empty or over the size limit.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpPost("upload-link")]
    [ProducesResponseType(typeof(UploadLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult UploadLink([FromBody] UploadLinkRequest request)
    {
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
    /// Step 2 of 2: records an object already in the bucket - the only step that writes the
    /// row. Size is read back from the object. An unconfirmed object stays an orphan.
    /// </summary>
    /// <response code="201">The row exists; the file is now in the listing.</response>
    /// <response code="400">The object is empty or over the size limit; it has been removed.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such object in the bucket.</response>
    /// <response code="409">This key was already confirmed.</response>
    [HttpPost("{fileName}/confirm")]
    [ProducesResponseType(typeof(UploadedAssetResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmUpload(
        string fileName,
        [FromBody] ConfirmUploadRequest request,
        CancellationToken cancellationToken)
    {
        // A retried confirm must not leave two rows on one object.
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
            // The bytes are already in the bucket; without a row nothing would look at them again.
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

        // Same SaveChanges as the asset row, so job and file commit together.
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

    /// <summary>Deletes one stored file, binning its note if it has one.</summary>
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

        // The note describes this file, so it goes too - into the bin, so links can say "deleted".
        var note = await _database.Notes
            .FirstOrDefaultAsync(note => note.SourceAssetId == record.Id, cancellationToken);

        if (note is not null)
        {
            note.DeletedAtUtc = DateTime.UtcNow;
        }

        // Row first: a leftover object beats a row pointing at nothing if the delete below fails.
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
    /// Re-queues a file the pipeline failed on, skipped, or whose note has been binned.
    /// (For a file that still has a note, use process-again on the note.)
    /// </summary>
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
