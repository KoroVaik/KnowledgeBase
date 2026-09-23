using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker.FaceRecognitionComparison;

public sealed class RecognitionComparisonModelFiles(IOptions<FaceRecognitionComparisonOptions> options)
{
    private const string MobileFaceNetHash = "9CC6E4A75F0E2BF0B1AED94578F144D15175F357BDC05E815E5C4A02B319EB4F";
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public async Task<string> EnsureMobileFaceNetAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.Value.ModelDirectory, "w600k_mbf.onnx");
        await _downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                await VerifyAsync(path, cancellationToken);
                return path;
            }

            Directory.CreateDirectory(options.Value.ModelDirectory);
            var archive = path + ".zip.part";
            var extracted = path + ".part";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
                using var response = await client.GetAsync(
                    "https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_sc.zip",
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (var target = File.Create(archive))
                    await response.Content.CopyToAsync(target, cancellationToken);
                using (var package = ZipFile.OpenRead(archive))
                {
                    var entry = package.Entries.Single(item => item.Name == "w600k_mbf.onnx");
                    await using var source = entry.Open();
                    await using var target = File.Create(extracted);
                    await source.CopyToAsync(target, cancellationToken);
                }
                await VerifyAsync(extracted, cancellationToken);
                File.Move(extracted, path, overwrite: true);
                return path;
            }
            finally
            {
                File.Delete(archive);
                File.Delete(extracted);
            }
        }
        finally { _downloadLock.Release(); }
    }

    private static async Task VerifyAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        if (hash != MobileFaceNetHash)
            throw new InvalidDataException("Recognition-comparison model checksum mismatch. Remove the cached model and retry.");
    }
}
