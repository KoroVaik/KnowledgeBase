using KnowledgeBase.Core.Ai;

namespace KnowledgeBase.Core.Pipeline.TagGrouping;

// The prompt for one grouping pass over the whole tag vocabulary - the batch counterpart to
// closestKnownTag in SourceNotePrompt (see Tag review in docs/database.md): same "model's call,
// never string distance" stance, run again later over tags that were never compared before
// because they were not both in the vocabulary on the same upload.
public static class TagGroupingPrompt
{
    public static AiTask TaskFor(IReadOnlyList<string> confirmedTags, IReadOnlyList<string> unconfirmedTags) =>
        new(System, UserPrompt(confirmedTags, unconfirmedTags), TagGroupingResult.Schema());

    private const string System =
        """
        You review the tag vocabulary of a personal knowledge base. You get two lists: tags the
        user has confirmed, and tags awaiting review (pipeline-invented, not yet judged).
        For every tag awaiting review, decide whether it means the same thing as one confirmed
        tag - the same real-world concept, not just a similar-looking string ("car" and
        "automobile" match; "Java" the language and "Java" the island do not).
        Return only JSON matching the schema: one entry per tag awaiting review.
        - tag: copied verbatim from the awaiting-review list.
        - closestConfirmedTag: the matching tag's name, copied verbatim from the confirmed list.
          Use "" when none of the confirmed tags mean the same thing.
        """;

    private static string UserPrompt(IReadOnlyList<string> confirmedTags, IReadOnlyList<string> unconfirmedTags) =>
        $"""
        Confirmed tags:
        {NameList(confirmedTags)}

        Tags awaiting review:
        {NameList(unconfirmedTags)}
        """;

    private static string NameList(IReadOnlyList<string> names) =>
        names.Count > 0 ? string.Join("\n", names.Select(name => $"- {name}")) : "(none)";
}
