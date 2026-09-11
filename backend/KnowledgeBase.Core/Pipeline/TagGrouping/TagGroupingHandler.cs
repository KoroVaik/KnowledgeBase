using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.TagGrouping;

// Re-runs the closest-confirmed-tag suggestion (see docs/database.md, Tag review) over every
// unconfirmed tag in one model call, not just ones invented in the same run as a source note.
// Writes SuggestedMergeIntoId directly; produces no note.
public sealed class TagGroupingHandler(KnowledgeBaseDbContext database, IContentAnalyzer analyzer) : IPipelineHandler
{
    public JobKind Kind => JobKind.GroupTags;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var tags = await database.Tags.ToListAsync(cancellationToken);
        var confirmed = tags.Where(tag => tag.Confirmed).ToList();
        var unconfirmed = tags.Where(tag => !tag.Confirmed).ToList();

        if (confirmed.Count == 0 || unconfirmed.Count == 0)
        {
            throw new SkippableContentException(
                "Nothing to group: needs at least one confirmed and one unreviewed tag.");
        }

        var task = TagGroupingPrompt.TaskFor(
            confirmed.Select(tag => tag.Name).ToList(),
            unconfirmed.Select(tag => tag.Name).ToList());

        var result = await analyzer.RunAsync<TagGroupingResult>(task, cancellationToken);

        var confirmedByName = confirmed.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);
        var unconfirmedByName = unconfirmed.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var suggestion in result.Suggestions ?? [])
        {
            if (string.IsNullOrWhiteSpace(suggestion.ClosestConfirmedTag))
            {
                continue;
            }

            if (!unconfirmedByName.TryGetValue(suggestion.Tag.Trim(), out var tag))
            {
                continue;
            }

            if (!confirmedByName.TryGetValue(suggestion.ClosestConfirmedTag.Trim(), out var target))
            {
                continue;
            }

            tag.SuggestedMergeIntoId = target.Id;
        }

        return null;
    }
}
