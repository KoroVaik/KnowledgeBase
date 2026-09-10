using System.Text.Json;
using KnowledgeBase.Core.Persistence;

namespace KnowledgeBase.Core.Pipeline.Synthesis;

// ProcessingJob.Payload for a BuildSynthesis job. The endpoint resolves the input notes and
// the group; the handler just merges whatever it is given, so L3 (Index over Synthesis notes)
// is the same handler with a different list.
public sealed record SynthesisJobPayload(
    NoteKind TargetKind,
    string GroupLabel,
    IReadOnlyList<string> InputNoteIds)
{
    public string Serialize() => JsonSerializer.Serialize(this, PipelineJson.Options);

    public static SynthesisJobPayload Deserialize(string? payload) =>
        JsonSerializer.Deserialize<SynthesisJobPayload>(
            payload ?? throw new InvalidOperationException("A synthesis job has no payload."),
            PipelineJson.Options)
        ?? throw new InvalidOperationException("A synthesis job has an unreadable payload.");
}
