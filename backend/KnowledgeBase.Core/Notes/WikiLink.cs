using System.Text.RegularExpressions;

namespace KnowledgeBase.Core.Notes;

/// <summary>
/// The [[title]] links inside a note body.
/// </summary>
/// <remarks>
/// The body is the record of what the note says; the NoteLinks rows are an index over it. Both
/// the reader and the pipeline start here, from the text itself.
/// </remarks>
public static partial class WikiLink
{
    // No newlines inside a link: an unclosed [[ must not swallow the rest of the note.
    [GeneratedRegex(@"\[\[([^\[\]\r\n]+)\]\]")]
    private static partial Regex Pattern();

    /// <summary>
    /// Every distinct title the body links to, in the order it first mentions them.
    /// </summary>
    public static IReadOnlyList<string> TitlesIn(string body) =>
        Pattern()
            .Matches(body)
            // Obsidian writes [[title|what to show]]. Nothing here produces that yet, but a
            // body that carries one must not turn the whole thing into a title.
            .Select(match => match.Groups[1].Value.Split('|')[0].Trim())
            .Where(title => title.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
