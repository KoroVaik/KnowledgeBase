using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline.Extraction;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Core.Pipeline.SourceNotes;

// Turns one uploaded file into a Source note. A re-run replaces the note rather than adding a
// sibling; inbound links follow the note to the new version.
public sealed class SourceNoteHandler(
    KnowledgeBaseDbContext database,
    IContentAnalyzer analyzer,
    IAssetContentReader reader,
    SourceExtractorSelector extractors,
    IOptions<PipelineOptions> options) : IPipelineHandler
{
    private readonly int _maxSourceChars = options.Value.MaxSourceChars;

    public JobKind Kind => JobKind.BuildSourceNote;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var assetId = job.AssetId
            ?? throw new InvalidOperationException($"Job {job.Id} is a source job with no asset.");

        var asset = await database.Assets.FirstOrDefaultAsync(record => record.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset {assetId} no longer exists.");

        var kind = ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName)
            ?? throw new InvalidOperationException($"{asset.OriginalFileName} is not a supported type.");

        var extractor = extractors.For(kind);

        // A re-run replaces the note, not adds a sibling; the old one is not offered as a link target.
        var previous = await database.Notes
            .FirstOrDefaultAsync(existing => existing.SourceAssetId == asset.Id, cancellationToken);

        var titles = await database.Notes.Select(note => note.Title).ToListAsync(cancellationToken);
        var existingTags = await database.Tags.ToListAsync(cancellationToken);

        if (previous is not null)
        {
            titles.Remove(previous.Title);
        }

        var bytes = await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken);
        var extracted = await extractor.ExtractAsync(
            new SourceAsset(bytes, asset.ContentType, asset.OriginalFileName), cancellationToken);

        // Caught here, not left to Ollama's silent truncation: a bounded slice would make a
        // note that looks fine but came from a fraction of the source.
        if (extracted.Text is { Length: var length } && length > _maxSourceChars)
        {
            throw new ContentTooLargeException(
                $"The text is {length:N0} characters; the pipeline handles up to "
                + $"{_maxSourceChars:N0}. Split it into smaller files.");
        }

        var task = SourceNotePrompt.TaskFor(extracted, titles, existingTags);
        var draft = await analyzer.RunAsync<NoteDraft>(task, cancellationToken);

        if (string.IsNullOrWhiteSpace(draft.Title) || draft.Tags is null || draft.Tags.All(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"Ollama returned an unusable analysis: {draft}");
        }

        var now = DateTime.UtcNow;

        if (previous is not null)
        {
            // On its own: a re-run usually reuses the title, and the live-title unique index
            // would reject the insert if EF ordered it before this update.
            previous.DeletedAtUtc = now;
            await database.SaveChangesAsync(cancellationToken);
        }

        var note = new Note
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = NoteKind.Source,
            Title = NoteWriter.UniqueTitle(draft.Title, titles),
            Body = draft.MarkdownBody,
            SourceAssetId = asset.Id,
            SourceFileName = asset.OriginalFileName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await NoteWriter.CommitAsync(database, note, draft, titles, previous, cancellationToken);
        NoteWriter.AttachProposedTags(database, note, draft, existingTags);

        return note;
    }
}
