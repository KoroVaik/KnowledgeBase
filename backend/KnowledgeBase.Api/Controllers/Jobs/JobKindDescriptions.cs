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
        JobKind.BuildSynthesis =>
            "Combines several existing notes into one synthesis (or index) note.",
        JobKind.GroupTags =>
            "Re-checks every unconfirmed tag and suggests the closest confirmed tag to merge it into.",
        JobKind.SuggestTagParents =>
            "Suggests a parent for every confirmed tag that has no parent and no pending suggestion.",
    };
#pragma warning restore CS8524
}
