using System.Text.Json;
using KnowledgeBase.Api.Controllers.Assets.Contracts;
using KnowledgeBase.Core.Observability;
using KnowledgeBase.Core.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace KnowledgeBase.Api.Controllers.Assets;

public sealed partial class AssetsController
{
    /// <summary>Signs up to 50 downloads, with an independent result for every item.</summary>
    [HttpPost("download-links")]
    [RequestSizeLimit(131072)]
    [ProducesResponseType(typeof(AssetBatchResult<AssetLinkResponse>[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadLinks(AssetBatchRequest<DownloadLinkRequest> request, CancellationToken cancellationToken)
    {
        if (DuplicateIds(request.Items)) return BadRequest(new { error = "Item ids must be unique." });
        var names = request.Items.Select(item => item.Request.FileName).Distinct().ToArray();
        var records = await _database.Assets.AsNoTracking().Where(asset => names.Contains(asset.StoredFileName))
            .ToDictionaryAsync(asset => asset.StoredFileName, cancellationToken);
        var results = new AssetBatchResult<AssetLinkResponse>[request.Items.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, request.Items.Length),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (index, token) =>
            {
                var item = request.Items[index];
                try
                {
                    if (!records.TryGetValue(item.Request.FileName, out var record)
                        || await _storage.GetAsync(record.StoredFileName, token) is null)
                    {
                        results[index] = new(item.Id, 404, null, "No such file.");
                        return;
                    }
                    var link = _signer.SignDownload(record.StoredFileName, record.OriginalFileName, record.ContentType);
                    results[index] = new(item.Id, 200, new(link.Url, link.ExpiresAtUtc), null);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Download link failed for {AssetId}", AssetFileName.IdOf(item.Request.FileName));
                    results[index] = new(item.Id, 503, null, "Could not prepare the download link.");
                }
            });
        return Ok(results);
    }

    /// <summary>Reserves up to 50 upload keys; bytes are still PUT directly to the bucket.</summary>
    [HttpPost("upload-links")]
    [RequestSizeLimit(131072)]
    [ProducesResponseType(typeof(AssetBatchResult<UploadLinkResponse>[]), StatusCodes.Status200OK)]
    public IActionResult UploadLinks(AssetBatchRequest<UploadLinkRequest> request)
    {
        if (DuplicateIds(request.Items)) return BadRequest(new { error = "Item ids must be unique." });
        var results = new List<AssetBatchResult<UploadLinkResponse>>();
        foreach (var item in request.Items)
        {
            using var activity = OperationContext.StartActivity("assets.upload-link", item.TraceParent);
            using var operation = OperationContext.Push(OperationContext.Current with
            { UploadId = item.Id, UploadBatchId = OperationContext.Identifier(item.UploadBatchId) ?? OperationContext.Current.UploadBatchId });
            using var uploadScope = LogContext.PushProperty("UploadId", item.Id);
            using var batchScope = LogContext.PushProperty("UploadBatchId", OperationContext.Current.UploadBatchId);
            try
            {
                var result = BatchResult<UploadLinkResponse>(item.Id, UploadLink(item.Request));
                results.Add(result);
                logger.LogInformation("Upload link prepared: {UploadId} {AssetId} {StatusCode}", item.Id,
                    result.Value is null ? null : AssetFileName.IdOf(result.Value.FileName), result.Status);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Upload link failed for {UploadId}", item.Id);
                results.Add(new(item.Id, 503, null, "Could not prepare the upload link."));
            }
        }
        return Ok(results);
    }

    /// <summary>Confirms up to 50 uploads independently. Repeating a confirmed key returns the existing row.</summary>
    [HttpPost("confirm-batch")]
    [RequestSizeLimit(131072)]
    [ProducesResponseType(typeof(AssetBatchResult<UploadedAssetResponse>[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmBatch(AssetBatchRequest<ConfirmBatchItem> request, CancellationToken cancellationToken)
    {
        if (DuplicateIds(request.Items)) return BadRequest(new { error = "Item ids must be unique." });
        var results = new List<AssetBatchResult<UploadedAssetResponse>>();
        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var activity = OperationContext.StartActivity("assets.confirm", item.TraceParent);
            using var operation = OperationContext.Push(OperationContext.Current with
            { UploadId = item.Id, UploadBatchId = OperationContext.Identifier(item.UploadBatchId) ?? OperationContext.Current.UploadBatchId });
            using var batchScope = LogContext.PushProperty("UploadBatchId", OperationContext.Current.UploadBatchId);
            using var uploadScope = LogContext.PushProperty("UploadId", item.Id);
            using var assetScope = LogContext.PushProperty("AssetId", AssetFileName.IdOf(item.Request.FileName));
            try
            {
                var result = await ConfirmUpload(item.Request.FileName,
                    new ConfirmUploadRequest(item.Request.OriginalFileName, item.Request.ContentType), cancellationToken);
                var outcome = BatchResult<UploadedAssetResponse>(item.Id, result);
                results.Add(outcome);
                logger.LogInformation("Upload confirmation completed: {UploadId} {StatusCode}", item.Id, outcome.Status);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Batch confirmation failed for {UploadId}", item.Id);
                results.Add(new(item.Id, 503, null, "Could not register the file. Retry this item."));
            }
            finally { _database.ChangeTracker.Clear(); }
        }
        return Ok(results);
    }

    private static bool DuplicateIds<T>(BatchItem<T>[] items) => items.Select(item => item.Id).Distinct().Count() != items.Length;

    private static AssetBatchResult<T> BatchResult<T>(string id, IActionResult result)
    {
        var status = result switch { ObjectResult obj => obj.StatusCode ?? 200, StatusCodeResult code => code.StatusCode, _ => 500 };
        if (result is ObjectResult { Value: T value } && status is >= 200 and < 300) return new(id, status, value, null);
        var error = "The item could not be processed.";
        if (result is ObjectResult { Value: not null } failure)
        {
            var body = JsonSerializer.SerializeToElement(failure.Value);
            if (body.TryGetProperty("error", out var message)) error = message.GetString() ?? error;
        }
        return new(id, status, default, error);
    }
}
