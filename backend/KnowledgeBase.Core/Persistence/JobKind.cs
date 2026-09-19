namespace KnowledgeBase.Core.Persistence;

// What a ProcessingJob asks the worker to build. Stored as a string (like NoteKind), so a
// new kind needs no migration. Each kind has one IPipelineHandler.
public enum JobKind
{
    // Turn one uploaded file into a Source note. Payload is unused; the file is AssetId.
    BuildSourceNote,

    // Detect faces and create identity candidates. It does not call Ollama.
    AnalyzeFaces,

    // Calculates an exact byte fingerprint before face analysis chooses one duplicate representative.
    FingerprintAsset,

    // Re-score stored faces against the confirmed reference set after it changed. Detects nothing
    // and reads no file; AssetId and Payload unused. Superseded by ClusterFaces: the value stays
    // because old rows exist in the database, and they run the clustering handler.
    RescoreFaces,

    // Groups every open face of the archive into review rows: joins confirmed people and ignored
    // groups, clusters the rest. Runs once per batch (skips while detection jobs are active).
    // Detects nothing and reads no file; AssetId and Payload unused.
    ClusterFaces,

    // One self-healing model migration: re-embeds faces stored by an older embedder, assigns face
    // identities to occurrences without one, requeues detection for photos analysed by an older
    // pipeline version, then regroups faces. AssetId and Payload unused; the worker enqueues it
    // itself on start when the check finds any gap.
    MigrateFaceModels,

    // Generate a CLIP scene vector and propose reviewed location candidates.
    AnalyzeScenes,

    // Uses the VLM with reviewed person/location context to extract cautious scene observations.
    AnalyzeSceneObservations,

    // Groups canonical photos into reviewable possible events from reviewed archive evidence.
    AnalyzeEventCandidates,

    // Aggregate several notes into one Synthesis (or Index) note. Payload carries the input
    // note ids and the target kind; AssetId is null.
    BuildSynthesis,

    // Re-run the closest-confirmed-tag suggestion over every unconfirmed tag, not just ones
    // invented in the same run as a source note. Produces no note; AssetId and Payload unused.
    GroupTags,

    // Find a parent for every confirmed tag that has none yet and no pending suggestion.
    // Produces no note; AssetId and Payload unused.
    SuggestTagParents,
}
