using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Core.Pipeline.Synthesis;

// Merges the notes named in the job payload into one Synthesis (or Index) note. A re-run
// replaces the note for the same (kind, group); inbound links follow it to the new version.
public sealed class SynthesisHandler(
    KnowledgeBaseDbContext database,
    IContentAnalyzer analyzer,
    IOptions<PipelineOptions> options) : IPipelineHandler
{
    private readonly int _maxChars = options.Value.MaxSynthesisChars;

    public JobKind Kind => JobKind.BuildSynthesis;

    public async Task<Note> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var payload = SynthesisJobPayload.Deserialize(job.Payload);

        var inputs = await database.Notes
            .Where(note => payload.InputNoteIds.Contains(note.Id))
            .Select(note => new { note.Id, note.Title, note.Body })
            .ToListAsync(cancellationToken);

        if (inputs.Count < 2)
        {
            throw new SkippableContentException(
                "Fewer than 2 of the chosen notes are still available to merge.");
        }

        var totalChars = inputs.Sum(note => note.Body.Length);

        if (totalChars > _maxChars)
        {
            throw new ContentTooLargeException(
                $"The notes add up to {totalChars:N0} characters; a synthesis handles up to "
                + $"{_maxChars:N0}. Narrow the group or split the notes.");
        }

        var previous = await database.Notes.FirstOrDefaultAsync(
            note => note.Kind == payload.TargetKind && note.SynthesisGroup == payload.GroupLabel,
            cancellationToken);

        var allTitles = await database.Notes.Select(note => note.Title).ToListAsync(cancellationToken);
        var existingTags = await database.Tags.ToListAsync(cancellationToken);

        if (previous is not null)
        {
            allTitles.Remove(previous.Title);
        }

        // The synthesis absorbs its inputs, so it links outward only - not back at them.
        var inputTitles = inputs.Select(note => note.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkableTitles = allTitles.Where(title => !inputTitles.Contains(title)).ToList();

        var topic = payload.TargetKind == NoteKind.Index
            ? "the whole knowledge base"
            : $"the tag \"{payload.GroupLabel}\"";

        var task = SynthesisPrompt.TaskFor(
            topic,
            inputs.Select(note => new SynthesisPrompt.Input(note.Title, NoteWriter.WithoutRelated(note.Body))).ToList(),
            linkableTitles,
            existingTags.Select(tag => tag.Name).ToList());

        var draft = await analyzer.RunAsync<NoteDraft>(task, cancellationToken);

        if (string.IsNullOrWhiteSpace(draft.Title) || draft.Tags is null || draft.Tags.All(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"Ollama returned an unusable synthesis: {draft}");
        }

        var now = DateTime.UtcNow;

        if (previous is not null)
        {
            previous.DeletedAtUtc = now;
            await database.SaveChangesAsync(cancellationToken);
        }

        var note = new Note
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = payload.TargetKind,
            Title = NoteWriter.UniqueTitle(draft.Title, allTitles),
            Body = draft.MarkdownBody,
            SynthesisGroup = payload.GroupLabel,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await NoteWriter.CommitAsync(database, note, draft, existingTags, linkableTitles, previous, cancellationToken);

        foreach (var input in inputs)
        {
            database.SynthesisSources.Add(new SynthesisSource
            {
                SynthesisNoteId = note.Id,
                InputNoteId = input.Id,
            });
        }

        return note;
    }
}
