namespace KnowledgeBase.Core.Persistence;

public enum NoteKind
{
    // Describes one uploaded file and is owned by it: deleting the file bins the note, because
    // a structured description of a file that is gone describes nothing.
    Source,

    // Written from many source notes. Absorbs their content rather than pointing at it, so it
    // survives every file it was built from.
    Synthesis,
}
