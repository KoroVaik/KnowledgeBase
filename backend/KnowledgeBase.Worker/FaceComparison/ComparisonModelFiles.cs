using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker.FaceComparison;

public sealed class ComparisonModelFiles(IOptions<FaceComparisonOptions> options)
{
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    private const string ScrfdHash = "5838F7FE053675B1C7A08B633DF49E7AF5495CEE0493C7DCF6697200B85B5B91";
    private const string YuNetHash = "8F2383E4DD3CFBB4553EA8718107FC0423210DC964F9F4280604804ED2552FA4";

    public static string HashFor(string modelId) => modelId == "scrfd-10g" ? ScrfdHash : YuNetHash;

    public async Task<string> EnsureAsync(string modelId, CancellationToken cancellationToken)
    {
        if (modelId is not ("scrfd-10g" or "yunet")) throw new ArgumentOutOfRangeException(nameof(modelId));
        var fileName = modelId == "scrfd-10g" ? "scrfd-10g.onnx" : "yunet-2023mar.onnx";
        var path = Path.Combine(options.Value.ModelDirectory, fileName);
        await _downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                await VerifyAsync(path, HashFor(modelId), cancellationToken);
                return path;
            }
            Directory.CreateDirectory(options.Value.ModelDirectory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
            var extracted = temporary + ".onnx";
            try
            {
                var url = modelId == "scrfd-10g"
                    ? "https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip"
                    : "https://media.githubusercontent.com/media/opencv/opencv_zoo/f12e12798e8314f7c074a6656816c048dcc95b7a/models/face_detection_yunet/face_detection_yunet_2023mar.onnx";
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (var file = File.Create(temporary))
                    await response.Content.CopyToAsync(file, cancellationToken);
                if (modelId == "scrfd-10g")
                {
                    using var archive = ZipFile.OpenRead(temporary);
                    var entry = archive.Entries.Single(item => item.Name == "det_10g.onnx");
                    await using var input = entry.Open();
                    await using var output = File.Create(extracted);
                    await input.CopyToAsync(output, cancellationToken);
                }
                else File.Move(temporary, extracted);
                await VerifyAsync(extracted, HashFor(modelId), cancellationToken);
                File.Move(extracted, path, overwrite: true);
                return path;
            }
            finally
            {
                File.Delete(temporary);
                File.Delete(extracted);
            }
        }
        finally { _downloadLock.Release(); }
    }

    private static async Task VerifyAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        if (hash != expected) throw new InvalidDataException("Comparison model checksum mismatch. Remove the cached model and retry.");
    }
}
