using Amazon.S3;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Core.Storage;

public sealed class S3AssetContentReader(IAmazonS3 client, IOptions<S3StorageOptions> options)
    : IAssetContentReader
{
    private readonly IAmazonS3 _client = client;
    private readonly string _bucketName = options.Value.BucketName;

    public async Task<byte[]> ReadBytesAsync(string storedFileName, CancellationToken cancellationToken)
    {
        using var response = await _client.GetObjectAsync(_bucketName, storedFileName, cancellationToken);
        using var buffer = new MemoryStream();

        await response.ResponseStream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
