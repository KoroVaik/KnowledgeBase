using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Backend.Infrastructure.Storage;

public sealed class S3AssetStorage : IAssetStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;

    public S3AssetStorage(IAmazonS3 client, IOptions<S3StorageOptions> options)
    {
        _client = client;
        _bucketName = options.Value.BucketName;
    }

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
            },
            cancellationToken);

        return new StoredAsset(id, key);
    }
}
