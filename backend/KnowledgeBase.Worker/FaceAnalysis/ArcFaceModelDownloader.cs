using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker.FaceAnalysis;

public sealed class ArcFaceModelDownloader(IOptions<FaceAnalysisOptions> options)
{
    private const string ModelEntryName = "w600k_r50.onnx";

    // Only an absolute path (the deployed container's models volume) is fetched; relative paths
    // are the local-dev layout, where the file is placed by hand.
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var targetPath = settings.RecognitionModelPath;
        if (!Path.IsPathRooted(targetPath) || File.Exists(targetPath)) return;

        Console.WriteLine($"[arcface] Model not found at {targetPath}; downloading {settings.ModelDownloadUrl}");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var archivePath = targetPath + ".zip.part";
        var partialModelPath = targetPath + ".part";
        try
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var response = await client.GetAsync(settings.ModelDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var archiveFile = File.Create(archivePath);
                await response.Content.CopyToAsync(archiveFile, cancellationToken);
            }

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name == ModelEntryName)
                    ?? throw new InvalidOperationException($"{ModelEntryName} is not in the downloaded archive.");
                entry.ExtractToFile(partialModelPath, overwrite: true);
            }

            // Renamed last, so a half-written model can never pass the File.Exists check.
            File.Move(partialModelPath, targetPath, overwrite: true);
            Console.WriteLine("[arcface] Model downloaded.");
        }
        finally
        {
            File.Delete(archivePath);
            File.Delete(partialModelPath);
        }
    }
}
