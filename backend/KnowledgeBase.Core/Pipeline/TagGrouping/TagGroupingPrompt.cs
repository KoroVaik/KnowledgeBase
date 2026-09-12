using KnowledgeBase.Core.Ai;

namespace KnowledgeBase.Core.Pipeline.TagGrouping;

// The prompt for one grouping pass over the whole tag vocabulary - the batch counterpart to
// closestKnownTag in SourceNotePrompt (see Tag review in docs/database.md): same "model's call,
// never string distance" stance, run again later over tags that were never compared before
// because they were not both in the vocabulary on the same upload.
//
// The matching pool is the WHOLE vocabulary, not just confirmed tags - two tags invented on
// different uploads, both still awaiting review, are otherwise never compared to each other at
// all (see docs/ai-pipeline.md, "Tag grouping pass").
public static class TagGroupingPrompt
{
    public static AiTask TaskFor(IReadOnlyList<string> confirmedTags, IReadOnlyList<string> unconfirmedTags) =>
        new(System, UserPrompt(confirmedTags, unconfirmedTags), TagGroupingResult.Schema());

    private const string System =
        """
        You review the tag vocabulary of a personal knowledge base. You get two lists: tags the
        user has confirmed, and tags awaiting review (pipeline-invented, not yet judged).
        For every tag awaiting review, decide whether it means the same thing as another tag in
        either list - the same real-world concept, not just a similar-looking string ("car" and
        "automobile" match; "Java" the language and "Java" the island do not). The match can be a
        confirmed tag or another tag still awaiting review; it is never the tag itself.
        Return only JSON matching the schema: one entry per tag awaiting review.
        - tag: copied verbatim from the awaiting-review list.
        - closestMatchingTag: the matching tag's name, copied verbatim from whichever list it
          came from. Use "" when nothing in either list means the same thing.
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
