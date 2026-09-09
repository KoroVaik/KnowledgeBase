using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Backend.Infrastructure.Storage;

public sealed class S3AssetStorage(IAmazonS3 client, IOptions<S3StorageOptions> options) : IAssetStorage
{
    private readonly IAmazonS3 _client = client;
    private readonly string _bucketName = options.Value.BucketName;

    public async Task<StoredAsset> SaveAsync(
        Stream content,
        string? originalFileName,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var key = AssetFileName.For(id, originalFileName);

        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = content,

                // Cloudflare R2 answers chunked payload signing with
                // "STREAMING-AWS4-HMAC-SHA256-PAYLOAD not implemented"; one signature over the
                // whole body is understood by every S3-compatible provider.
                UseChunkEncoding = false,
            },
            cancellationToken);

        return new StoredAsset(id, key);
    }

    public async Task<IReadOnlyList<AssetSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var assets = new List<AssetSummary>();
        var request = new ListObjectsV2Request { BucketName = _bucketName };

        // A bucket answers at most 1000 keys per call and says so with IsTruncated; without
        // the loop the listing would silently stop at the first page.
        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request, cancellationToken);

            assets.AddRange((response.S3Objects ?? []).Select(item => new AssetSummary(
                item.Key,
                item.Size ?? 0,
                AsUtc(item.LastModified))));

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        return assets.OrderByDescending(asset => asset.LastModifiedUtc).ToList();
    }

    public async Task<AssetSummary?> GetAsync(string fileName, CancellationToken cancellationToken)
    {
        if (!AssetFileName.IsSafe(fileName))
        {
            return null;
        }

        try
        {
            var metadata = await _client.GetObjectMetadataAsync(_bucketName, fileName, cancellationToken);

            return new AssetSummary(fileName, metadata.ContentLength, AsUtc(metadata.LastModified));
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream?> OpenReadAsync(string fileName, CancellationToken cancellationToken)
    {
        if (!AssetFileName.IsSafe(fileName))
        {
            return null;
        }

        try
        {
            var response = await _client.GetObjectAsync(_bucketName, fileName, cancellationToken);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken)
    {
        if (!AssetFileName.IsSafe(fileName))
        {
            return false;
        }

        // DELETE on S3 answers 204 whether or not the key was there, so the existence check
        // is what makes a 404 possible at all.
        try
        {
            await _client.GetObjectMetadataAsync(_bucketName, fileName, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await _client.DeleteObjectAsync(_bucketName, fileName, cancellationToken);

        return true;
    }

    private static DateTimeOffset AsUtc(DateTime? timestamp) =>
        timestamp is null
            ? default
            : new DateTimeOffset(DateTime.SpecifyKind(timestamp.Value, DateTimeKind.Utc), TimeSpan.Zero);
}
