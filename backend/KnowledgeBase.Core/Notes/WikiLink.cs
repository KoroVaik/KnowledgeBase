using System.Text.RegularExpressions;

namespace KnowledgeBase.Core.Notes;

/// <summary>The [[title]] links inside a note body. The body is the record; NoteLinks indexes it.</summary>
public static partial class WikiLink
{
    // No newlines inside a link: an unclosed [[ must not swallow the rest of the note.
    [GeneratedRegex(@"\[\[([^\[\]\r\n]+)\]\]")]
    private static partial Regex Pattern();

    /// <summary>Every distinct title the body links to, in first-mention order.</summary>
    public static IReadOnlyList<string> TitlesIn(string body) =>
        Pattern()
            .Matches(body)
            // Keep the title half of Obsidian's [[title|shown]] if a body ever carries one.
            .Select(match => match.Groups[1].Value.Split('|')[0].Trim())
            .Where(title => title.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
