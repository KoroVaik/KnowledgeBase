using KnowledgeBase.Core.Persistence;

namespace KnowledgeBase.Api.Controllers.Jobs;

// Kept in code, not a lookup table: JobKind is stored as a string precisely so a new kind needs
// no migration. No default arm on purpose - a new named kind without a description is CS8509;
// only CS8524 (casted unnamed values) is silenced.
internal static class JobKindDescriptions
{
#pragma warning disable CS8524
    public static string For(JobKind kind) => kind switch
    {
        JobKind.BuildSourceNote =>
            "Reads one uploaded file with the AI model and turns it into a source note with tags.",
        JobKind.AnalyzeFaces =>
            "Detects faces in one image and stores them for grouping into people.",
        JobKind.CompareFaceDetectors =>
            "Compares three face detectors on one photo, separately from person suggestions.",
        JobKind.CompareFaceRecognizers =>
            "Runs three face recognizers over detected faces, separately from person suggestions.",
        JobKind.FingerprintAsset =>
            "Calculates an exact file fingerprint so duplicate images can share one face analysis.",
        JobKind.RescoreFaces =>
            "Legacy face re-score from before grouping; now regroups unreviewed faces by person.",
        JobKind.ClusterFaces =>
            "Groups every unreviewed face by person: joins confirmed people and ignored groups, clusters the rest.",
        JobKind.MigrateFaceModels =>
            "Brings stored face data up to the current models — embeddings, detections and face identities — then regroups faces.",
        JobKind.AnalyzeScenes =>
            "Generates a CLIP scene vector and proposes the most similar reviewed locations.",
        JobKind.AnalyzeSceneObservations =>
            "Uses reviewed people and locations to extract cautious actions, interactions and scene details.",
        JobKind.AnalyzeEventCandidates =>
            "Groups photos from reviewed archive evidence into possible events for human review.",
        JobKind.BuildSynthesis =>
            "Combines several existing notes into one synthesis (or index) note.",
        JobKind.GroupTags =>
            "Re-checks every unconfirmed tag and suggests the closest confirmed tag to merge it into.",
        JobKind.SuggestTagParents =>
            "Suggests a parent for every confirmed tag that has no parent and no pending suggestion.",
    };
#pragma warning restore CS8524
}
