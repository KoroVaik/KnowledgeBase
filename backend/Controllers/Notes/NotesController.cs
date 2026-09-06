using Backend.Controllers.Notes.Contracts;
using Backend.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Controllers.Notes;

/// <summary>
/// Notes and the files they are built from.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class NotesController : ControllerBase
{
    private readonly IAssetStorage _storage;
    private readonly StorageOptions _options;

    public NotesController(IAssetStorage storage, IOptions<StorageOptions> options)
    {
        _storage = storage;
        _options = options.Value;
    }

    /// <summary>
    /// Stores an uploaded file and returns its metadata.
    /// </summary>
    /// <param name="file">The PDF or image to store.</param>
    /// <param name="cancellationToken">Cancels the copy if the client disconnects.</param>
    /// <returns>Metadata of the stored file.</returns>
    /// <remarks>
    /// A stub: the file is kept as is. No AI processing, no .md generation, no indexing yet.
    /// </remarks>
    /// <response code="200">The file is stored.</response>
    /// <response code="400">The file is empty or over the size limit.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(UploadedAssetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
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

        return Ok(new UploadedAssetResponse(
            stored.Id,
            stored.FileName,
            Path.GetFileName(file.FileName),
            file.ContentType,
            file.Length,
            DateTimeOffset.UtcNow));
    }
}
