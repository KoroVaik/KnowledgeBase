using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.TagHierarchy;

// Finds a parent for every tag that has none yet and no pending suggestion - confirmed or still
// awaiting review alike (same widened-pool reasoning as TagGroupingHandler: a tag can be placed
// under a not-yet-reviewed tag too, so the review UI can offer "confirm as a child of X" before
// the two-step confirm-then-place ever happens - see "Tag hierarchy" in docs/database.md). Writes
// TagParentSuggestion rows, never TagParent directly - a human accepts one through the existing
// AddParent path. Produces no note.
public sealed class TagHierarchyHandler(KnowledgeBaseDbContext database, IContentAnalyzer analyzer) : IPipelineHandler
{
    // A dismissed pair is revived (re-surfaced) if proposed again while under this cap; at the
    // cap it is left alone for good instead of being re-inserted or revived.
    private const int DeclineCap = 3;

    public JobKind Kind => JobKind.SuggestTagParents;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var allTags = await database.Tags.ToListAsync(cancellationToken);

        var parentedIds = await database.TagParents
            .Select(link => link.ChildId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var pendingIds = await database.TagParentSuggestions
            .Where(suggestion => !suggestion.Dismissed)
            .Select(suggestion => suggestion.ChildId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var unplaced = allTags
            .Where(tag => !parentedIds.Contains(tag.Id) && !pendingIds.Contains(tag.Id))
            .ToList();

        if (allTags.Count < 2 || unplaced.Count == 0)
        {
            throw new SkippableContentException(
                "Nothing to place: needs a tag with no parent and no pending suggestion.");
        }

        var unplacedIds = unplaced.Select(tag => tag.Id).ToHashSet();

        // A child only reaches this loop with an existing pair if that pair is dismissed -
        // pendingIds above already excludes any tag with an active (non-dismissed) suggestion.
        // A dismissed pair proposed again is revived rather than skipped, unless it hit the cap.
        var existingByPair = (await database.TagParentSuggestions
            .Where(suggestion => unplacedIds.Contains(suggestion.ChildId))
            .ToListAsync(cancellationToken))
            .ToDictionary(suggestion => (suggestion.ChildId, suggestion.ParentId));
        var seenNewPairs = new HashSet<(string ChildId, string ParentId)>();

        var task = TagHierarchyPrompt.TaskFor(
            allTags.Select(tag => tag.Name).ToList(),
            unplaced.Select(tag => tag.Name).ToList());

        var result = await analyzer.RunAsync<TagHierarchySuggestionResult>(task, cancellationToken);

        var byName = allTags.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);
        var unplacedByName = unplaced.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var suggestion in result.Suggestions ?? [])
        {
            if (!unplacedByName.TryGetValue(suggestion.Tag.Trim(), out var child))
            {
                continue;
            }

            foreach (var candidate in suggestion.Parents ?? [])
            {
                if (string.IsNullOrWhiteSpace(candidate.Name))
                {
                    continue;
                }

                if (!byName.TryGetValue(candidate.Name.Trim(), out var parent) || parent.Id == child.Id)
                {
                    continue;
                }

                if (existingByPair.TryGetValue((child.Id, parent.Id), out var existing))
                {
                    if (existing.DeclineCount < DeclineCap)
                    {
                        existing.Dismissed = false;
                        existing.Confidence = SuggestionConfidenceParsing.Parse(candidate.Confidence);
                    }

                    continue;
                }

                if (!seenNewPairs.Add((child.Id, parent.Id)))
                {
                    continue;
                }

                database.TagParentSuggestions.Add(new TagParentSuggestion
                {
                    ChildId = child.Id,
                    ParentId = parent.Id,
                    Dismissed = false,
                    DeclineCount = 0,
                    Confidence = SuggestionConfidenceParsing.Parse(candidate.Confidence),
                });
            }
        }

        return null;
    }
}
