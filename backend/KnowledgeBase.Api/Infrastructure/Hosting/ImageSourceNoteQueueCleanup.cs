using KnowledgeBase.Api.Controllers.Notes.Configuration;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Infrastructure.Hosting;

public static class ImageSourceNoteQueueCleanup
{
    public static async Task SkipPendingJobsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ImageSourceNotesOptions>>().Value;

        if (options.Enabled)
        {
            return;
        }

        var database = scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>();
        var jobs = await database.ProcessingJobs
            .Where(job => job.Kind == JobKind.BuildSourceNote
                && job.Status == ProcessingStatus.Pending
                && job.AssetId != null)
            .Join(
                database.Assets,
                job => job.AssetId,
                asset => asset.Id,
                (job, asset) => new { job, asset })
            .ToListAsync();

        foreach (var pair in jobs.Where(pair =>
                     ProcessableContent.Classify(pair.asset.ContentType, pair.asset.OriginalFileName) is ContentKind.Image))
        {
            pair.job.Status = ProcessingStatus.Skipped;
            pair.job.CompletedAtUtc = DateTime.UtcNow;
            pair.job.Error = "Image-to-note analysis is disabled.";
        }

        await database.SaveChangesAsync();
    }
}
