using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.TagGrouping;

// Re-runs the closest-matching-tag suggestion (see docs/database.md, Tag review) over every
// unconfirmed tag in one model call, not just ones invented in the same run as a source note.
// The match can be a confirmed tag or another still-unconfirmed one - two tags invented on
// different uploads are otherwise never compared to each other. Writes SuggestedMergeIntoId
// directly; produces no note.
public sealed class TagGroupingHandler(KnowledgeBaseDbContext database, IContentAnalyzer analyzer) : IPipelineHandler
{
    public JobKind Kind => JobKind.GroupTags;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var tags = await database.Tags.ToListAsync(cancellationToken);
        var confirmed = tags.Where(tag => tag.Confirmed).ToList();
        var unconfirmed = tags.Where(tag => !tag.Confirmed).ToList();

        if (unconfirmed.Count == 0 || tags.Count < 2)
        {
            throw new SkippableContentException(
                "Nothing to group: needs an unreviewed tag and something else in the vocabulary to compare it to.");
        }

        var task = TagGroupingPrompt.TaskFor(
            confirmed.Select(tag => tag.Name).ToList(),
            unconfirmed.Select(tag => tag.Name).ToList());

        var result = await analyzer.RunAsync<TagGroupingResult>(task, cancellationToken);

        var unconfirmedByName = unconfirmed.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);
        var byName = tags.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var suggestion in result.Suggestions ?? [])
        {
            if (string.IsNullOrWhiteSpace(suggestion.ClosestMatchingTag))
            {
                continue;
            }

            if (!unconfirmedByName.TryGetValue(suggestion.Tag.Trim(), out var tag))
            {
                continue;
            }

            if (!byName.TryGetValue(suggestion.ClosestMatchingTag.Trim(), out var target) || target.Id == tag.Id)
            {
                continue;
            }

            tag.SuggestedMergeIntoId = target.Id;
            tag.SuggestedMergeConfidence = SuggestionConfidenceParsing.Parse(suggestion.Confidence);
        }

        return null;
    }
}
