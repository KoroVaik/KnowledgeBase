using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.TagHierarchy;

// Finds a parent for every confirmed tag that has none yet and no pending suggestion (see Tag
// hierarchy in docs/database.md). Writes TagParentSuggestion rows, never TagParent directly - a
// human accepts one through the existing AddParent path. Produces no note.
public sealed class TagHierarchyHandler(KnowledgeBaseDbContext database, IContentAnalyzer analyzer) : IPipelineHandler
{
    public JobKind Kind => JobKind.SuggestTagParents;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var confirmed = await database.Tags.Where(tag => tag.Confirmed).ToListAsync(cancellationToken);

        var parentedIds = await database.TagParents
            .Select(link => link.ChildId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var pendingIds = await database.TagParentSuggestions
            .Where(suggestion => !suggestion.Dismissed)
            .Select(suggestion => suggestion.ChildId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var unplaced = confirmed
            .Where(tag => !parentedIds.Contains(tag.Id) && !pendingIds.Contains(tag.Id))
            .ToList();

        if (confirmed.Count < 2 || unplaced.Count == 0)
        {
            throw new SkippableContentException(
                "Nothing to place: needs a confirmed tag with no parent and no pending suggestion.");
        }

        var unplacedIds = unplaced.Select(tag => tag.Id).ToHashSet();

        // Pairs already on record for these tags (dismissed or not) - never re-propose a rejected
        // pair, and never insert a duplicate of a pending one.
        var existingPairs = await database.TagParentSuggestions
            .Where(suggestion => unplacedIds.Contains(suggestion.ChildId))
            .Select(suggestion => new { suggestion.ChildId, suggestion.ParentId })
            .ToListAsync(cancellationToken);
        var seen = existingPairs.Select(pair => (pair.ChildId, pair.ParentId)).ToHashSet();

        var task = TagHierarchyPrompt.TaskFor(
            confirmed.Select(tag => tag.Name).ToList(),
            unplaced.Select(tag => tag.Name).ToList());

        var result = await analyzer.RunAsync<TagHierarchySuggestionResult>(task, cancellationToken);

        var confirmedByName = confirmed.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);
        var unplacedByName = unplaced.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var suggestion in result.Suggestions ?? [])
        {
            if (!unplacedByName.TryGetValue(suggestion.Tag.Trim(), out var child))
            {
                continue;
            }

            foreach (var parentName in suggestion.Parents ?? [])
            {
                if (string.IsNullOrWhiteSpace(parentName))
                {
                    continue;
                }

                if (!confirmedByName.TryGetValue(parentName.Trim(), out var parent) || parent.Id == child.Id)
                {
                    continue;
                }

                if (!seen.Add((child.Id, parent.Id)))
                {
                    continue;
                }

                database.TagParentSuggestions.Add(
                    new TagParentSuggestion { ChildId = child.Id, ParentId = parent.Id, Dismissed = false });
            }
        }

        return null;
    }
}
