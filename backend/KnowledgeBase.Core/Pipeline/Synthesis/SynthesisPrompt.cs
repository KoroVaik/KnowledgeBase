using KnowledgeBase.Core.Ai;

namespace KnowledgeBase.Core.Pipeline.Synthesis;

// The prompt for merging several notes into one. The group is a runtime value (a tag name,
// or "the knowledge base" for the Index), so the system text is built, not a const.
public static class SynthesisPrompt
{
    public sealed record Input(string Title, string Body);

    public static AiTask TaskFor(
        string topic,
        IReadOnlyList<Input> notes,
        IReadOnlyList<string> linkableTitles) =>
        new(System(topic), UserPrompt(notes, linkableTitles), NoteDraft.Schema(withTags: false));

    private static string System(string topic) =>
        $"""
        You merge several notes from a personal knowledge base into one synthesis note and
        draft it in Markdown. The notes below are all about {topic}.
        Write one note that organises and connects what they say: group related points under
        headings, keep it faithful to the sources, and do not invent facts or add anything
        that is not in them.
        Return only JSON matching the schema.
        - title: a short, specific title for the merged note.
        - markdownBody: the merged note as clean Markdown. Do not add a "Related" or
          "See also" section and do not write [[wiki links]] in the body - links go in the
          links field only.
        - links: titles taken verbatim from the existing-notes list that this note is genuinely
          related to. Use [] when none apply. Never invent a title that is not in the list.
        """;

    private static string UserPrompt(
        IReadOnlyList<Input> notes,
        IReadOnlyList<string> linkableTitles)
    {
        var titles = linkableTitles.Count > 0
            ? string.Join("\n", linkableTitles.Select(title => $"- {title}"))
            : "(none yet)";

        var merged = string.Join(
            "\n\n",
            notes.Select(note => $"### {note.Title}\n{note.Body}"));

        return $"""
            Existing notes:
            {titles}

            Notes to merge:

            {merged}
            """;
    }
}
