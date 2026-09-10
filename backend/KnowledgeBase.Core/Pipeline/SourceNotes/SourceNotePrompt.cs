using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Pipeline.Extraction;

namespace KnowledgeBase.Core.Pipeline.SourceNotes;

// The prompt for turning one file into a Source note. Owned here, not in the analyzer -
// every pipeline brings its own.
public static class SourceNotePrompt
{
    public static AiTask TaskFor(
        ExtractedContent content,
        IReadOnlyList<string> existingTitles,
        IReadOnlyList<string> knownTags) =>
        new(System, UserPrompt(content, existingTitles, knownTags), NoteDraft.Schema(), content.Image);

    private const string System =
        """
        You turn a source into a note for a personal knowledge base and draft it in Markdown.
        The source is either text or an image; for an image, transcribe any text in it and
        describe what it shows, then write the note from that.
        Return only JSON matching the schema.
        - title: a short, specific title for this note.
        - tags: 1 to 5 tags, most relevant first. Prefer tags from the known list the user
          provides; only invent a new one when nothing on the list fits, and never invent
          more than one new tag. Each tag is one or two words.
        - markdownBody: the note as clean Markdown. Stay faithful to the source and do not
          invent facts.
        - links: titles taken verbatim from the existing-notes list that this note is genuinely
          related to. Use [] when none apply. Never invent a title that is not in the list.
        """;

    private static string UserPrompt(
        ExtractedContent content,
        IReadOnlyList<string> existingTitles,
        IReadOnlyList<string> knownTags)
    {
        var tags = knownTags.Count > 0 ? string.Join(", ", knownTags) : "(none yet)";

        var titles = existingTitles.Count > 0
            ? string.Join("\n", existingTitles.Select(title => $"- {title}"))
            : "(none yet)";

        var source = content.Text is { } text
            ? $"""
                Source text (between the markers):
                <<<BEGIN>>>
                {text}
                <<<END>>>
                """
            : "The source is the attached image.";

        return $"""
            Known tags: {tags}

            Existing notes:
            {titles}

            {source}
            """;
    }
}
