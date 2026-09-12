using KnowledgeBase.Core.Ai;

namespace KnowledgeBase.Core.Pipeline.TagHierarchy;

// The prompt for one placement pass: given the whole confirmed vocabulary and the subset of it
// with no parent yet, propose which other confirmed tag(s) each could go under (is-a). Same
// "model's call, never string distance" stance as TagGroupingPrompt - see Tag hierarchy in
// docs/database.md.
public static class TagHierarchyPrompt
{
    public static AiTask TaskFor(IReadOnlyList<string> confirmedTags, IReadOnlyList<string> unplacedTags) =>
        new(System, UserPrompt(confirmedTags, unplacedTags), TagHierarchySuggestionResult.Schema());

    private const string System =
        """
        You organise the tag vocabulary of a personal knowledge base into an is-a hierarchy
        (e.g. "Porsche" is a "Car"; "Car" is a "Vehicle"). You get the full list of confirmed
        tags, and a subset of it that has no parent yet.
        For every tag awaiting placement, name zero, one, or two other confirmed tags it is a
        more specific case of. A tag may have more than one parent when it genuinely belongs to
        more than one broader category (e.g. "Porsche" under both "Car" and "German brand").
        Only suggest a parent you are confident about - an empty list is correct when nothing
        confirmed is broader than the tag. Never suggest a tag as its own parent.
        Return only JSON matching the schema: one entry per tag awaiting placement.
        - tag: copied verbatim from the awaiting-placement list.
        - parents: names copied verbatim from the confirmed list, most confident first.
        """;

    private static string UserPrompt(IReadOnlyList<string> confirmedTags, IReadOnlyList<string> unplacedTags) =>
        $"""
        Confirmed tags:
        {NameList(confirmedTags)}

        Tags awaiting placement:
        {NameList(unplacedTags)}
        """;

    private static string NameList(IReadOnlyList<string> names) =>
        names.Count > 0 ? string.Join("\n", names.Select(name => $"- {name}")) : "(none)";
}
