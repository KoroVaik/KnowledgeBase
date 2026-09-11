namespace KnowledgeBase.Core.Persistence;

// What a ProcessingJob asks the worker to build. Stored as a string (like NoteKind), so a
// new kind needs no migration. Each kind has one IPipelineHandler.
public enum JobKind
{
    // Turn one uploaded file into a Source note. Payload is unused; the file is AssetId.
    BuildSourceNote,

    // Aggregate several notes into one Synthesis (or Index) note. Payload carries the input
    // note ids and the target kind; AssetId is null.
    BuildSynthesis,

    // Re-run the closest-confirmed-tag suggestion over every unconfirmed tag, not just ones
    // invented in the same run as a source note. Produces no note; AssetId and Payload unused.
    GroupTags,
}
