namespace KnowledgeBase.Core.Persistence;

public enum NoteKind
{
    // Describes one uploaded file and is owned by it: deleting the file bins the note, because
    // a structured description of a file that is gone describes nothing.
    Source,

    // Written from many source notes carrying one tag. Absorbs their content rather than
    // pointing at it, so it survives every file it was built from.
    Synthesis,

    // The single top-level note written from every Synthesis note - a table of contents for
    // the knowledge base.
    Index,
}
