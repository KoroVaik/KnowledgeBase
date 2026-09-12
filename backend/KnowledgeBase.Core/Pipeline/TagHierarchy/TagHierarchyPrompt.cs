using KnowledgeBase.Core.Ai;

namespace KnowledgeBase.Core.Pipeline.TagHierarchy;

// The prompt for one placement pass: given the whole tag vocabulary (confirmed and still
// awaiting review alike - same widened-pool reasoning as TagGroupingPrompt, see
// docs/ai-pipeline.md "Tag grouping pass") and the subset of it with no parent yet, propose
// which other tag(s) each could go under (is-a). Same "model's call, never string distance"
// stance as TagGroupingPrompt - see Tag hierarchy in docs/database.md.
public static class TagHierarchyPrompt
{
    public static AiTask TaskFor(IReadOnlyList<string> allTags, IReadOnlyList<string> unplacedTags) =>
        new(System, UserPrompt(allTags, unplacedTags), TagHierarchySuggestionResult.Schema());

    private const string System =
        """
        You organise the tag vocabulary of a personal knowledge base into an is-a hierarchy
        (e.g. "Porsche" is a "Car"; "Car" is a "Vehicle"). You get the full tag list - both
        tags the user has confirmed and tags still awaiting review - and a subset of it that
        has no parent yet.
        For every tag awaiting placement, name zero, one, or two other tags from the full list
        it is a more specific case of. A tag may have more than one parent when it genuinely
        belongs to more than one broader category (e.g. "Porsche" under both "Car" and "German
        brand"). Only suggest a parent you are at least somewhat confident about - an empty list
        is correct when nothing in the full list is broader than the tag. Never suggest a tag as
        its own parent.
        Return only JSON matching the schema: one entry per tag awaiting placement.
        - tag: copied verbatim from the awaiting-placement list.
        - parents: 0-2 entries, most confident first. Each entry:
          - name: copied verbatim from the full tag list.
          - confidence: "low", "medium", or "high" - how sure you are this is genuinely a
            broader category the tag belongs to, not just a loosely related one.
        """;

    private static string UserPrompt(IReadOnlyList<string> allTags, IReadOnlyList<string> unplacedTags) =>
        $"""
        All tags:
        {NameList(allTags)}

        Tags awaiting placement:
        {NameList(unplacedTags)}
        """;

    private static string NameList(IReadOnlyList<string> names) =>
        names.Count > 0 ? string.Join("\n", names.Select(name => $"- {name}")) : "(none)";
}
