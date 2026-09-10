using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline;

// Turns a local model's loosely-obeyed draft into rows we trust, the same way for every
// handler that writes a note. The model over-invents tags and links whatever the prompt
// says, so its lists are filtered here, not asked for more firmly (see docs/ai-pipeline.md).
internal static class NoteWriter
{
    // Adds the note, its filtered tags and links, and fixes links that named it. The caller
    // has already set the note's kind-specific fields, its unique Title and its raw Body, and
    // (for a replacement) binned the previous note in an earlier SaveChanges.
    public static async Task CommitAsync(
        KnowledgeBaseDbContext database,
        Note note,
        NoteDraft draft,
        List<Tag> existingTags,
        IReadOnlyList<string> priorTitles,
        Note? previous,
        CancellationToken cancellationToken)
    {
        var links = FilterLinks(draft.Links ?? [], priorTitles);

        note.Body = AppendRelated(note.Body, links);
        database.Notes.Add(note);

        AttachTags(database, note, draft.Tags ?? [], existingTags);

        var targetIds = await database.Notes
            .Where(existing => links.Contains(existing.Title))
            .ToDictionaryAsync(existing => existing.Title, existing => existing.Id, cancellationToken);

        foreach (var target in links)
        {
            database.NoteLinks.Add(new NoteLink
            {
                Id = Guid.NewGuid().ToString("N"),
                SourceNoteId = note.Id,
                TargetTitle = target,
                TargetNoteId = targetIds.GetValueOrDefault(target),
            });
        }

        // Links elsewhere that named this note before it existed. Tracked, so the fix rides
        // the insert's SaveChanges.
        var dangling = await database.NoteLinks
            .Where(link => link.TargetNoteId == null && link.TargetTitle == note.Title)
            .ToListAsync(cancellationToken);

        foreach (var link in dangling)
        {
            link.TargetNoteId = note.Id;
        }

        if (previous is not null)
        {
            // Inbound links follow the note, not the version. Only the id moves - TargetTitle
            // is literal text in the other body.
            var inherited = await database.NoteLinks
                .Where(link => link.TargetNoteId == previous.Id)
                .ToListAsync(cancellationToken);

            foreach (var link in inherited)
            {
                link.TargetNoteId = note.Id;
            }
        }
    }

    // Keep only links to notes that already exist (exact title, case-insensitive), no dupes.
    public static List<string> FilterLinks(
        IReadOnlyList<string> proposed,
        IEnumerable<string> existingTitles)
    {
        var known = existingTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return proposed
            .Where(known.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // TEMP: the model gets the live titles as "do not reuse" but does not always comply, and
    // IX_Notes_Title (unique among the living) then rejects the insert. Suffix until free.
    // Proper handling is an Open item in docs/worker.md.
    public static string UniqueTitle(string proposed, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

        if (!used.Contains(proposed))
        {
            return proposed;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{proposed} ({suffix})";

            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    // Keep order, drop blanks/dupes, cap at 5, reuse an existing tag on an exact name match
    // (case-insensitive), allow at most one freshly invented tag per run. A genuinely new tag
    // lands Confirmed = false - shown everywhere, flagged for review.
    private static void AttachTags(
        KnowledgeBaseDbContext database,
        Note note,
        IReadOnlyList<string> proposed,
        List<Tag> existingTags)
    {
        var byName = existingTags.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 0;
        var invented = false;

        foreach (var candidate in proposed)
        {
            var name = candidate.Trim();

            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            if (!byName.TryGetValue(name, out var tag))
            {
                if (invented)
                {
                    continue;
                }

                tag = new Tag { Id = Guid.NewGuid().ToString("N"), Name = name, Confirmed = false };
                database.Tags.Add(tag);
                byName[name] = tag;
                invented = true;
            }

            database.NoteTags.Add(new NoteTag { NoteId = note.Id, TagId = tag.Id, Ordinal = ordinal++ });

            if (ordinal == 5)
            {
                break;
            }
        }
    }

    // Drops the trailing "## Related" block AppendRelated wrote, so feeding a note back into
    // the model (a synthesis over notes) does not make it copy that section and its [[links]].
    public static string WithoutRelated(string body)
    {
        var marker = body.IndexOf("\n## Related\n", StringComparison.Ordinal);

        return marker < 0 ? body : body[..marker].TrimEnd();
    }

    private static string AppendRelated(string body, IReadOnlyList<string> links)
    {
        if (links.Count == 0)
        {
            return body;
        }

        var list = string.Join("\n", links.Select(title => $"- [[{title}]]"));

        return $"{body.TrimEnd()}\n\n## Related\n\n{list}\n";
    }
}
