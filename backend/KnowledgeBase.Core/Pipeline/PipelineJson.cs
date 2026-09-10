using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnowledgeBase.Core.Pipeline;

// How ProcessingJob.Payload is written and read. Enums as strings so a stored payload stays
// legible and survives a reordered enum.
internal static class PipelineJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
