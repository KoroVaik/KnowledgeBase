using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.Synthesis;

// Puts a BuildSynthesis job in front of the worker. Does not save.
public static class SynthesisQueue
{
    // False when a job for the same (kind, group) is already pending or running - there is no
    // point queuing a second run of the same merge.
    public static async Task<bool> EnqueueAsync(
        KnowledgeBaseDbContext database,
        NoteKind targetKind,
        string groupLabel,
        IReadOnlyList<string> inputNoteIds,
        CancellationToken cancellationToken)
    {
        var active = await database.ProcessingJobs
            .Where(job => job.Kind == JobKind.BuildSynthesis
                && (job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running))
            .Select(job => job.Payload)
            .ToListAsync(cancellationToken);

        var alreadyQueued = active
            .Select(SynthesisJobPayload.Deserialize)
            .Any(payload => payload.TargetKind == targetKind
                && string.Equals(payload.GroupLabel, groupLabel, StringComparison.OrdinalIgnoreCase));

        if (alreadyQueued)
        {
            return false;
        }

        database.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = JobKind.BuildSynthesis,
            Payload = new SynthesisJobPayload(targetKind, groupLabel, inputNoteIds).Serialize(),
            CreatedAtUtc = DateTime.UtcNow,
            Status = ProcessingStatus.Pending,
        });

        return true;
    }
}
