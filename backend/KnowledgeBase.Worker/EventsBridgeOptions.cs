namespace KnowledgeBase.Worker;

public sealed class EventsBridgeOptions
{
    public const string SectionName = "Events";

    // The API's base URL. Empty leaves the bridge a no-op: PipelineWorker's change hints then
    // go nowhere, as before, and the front end picks new notes up on its next reload.
    public string ApiBaseUrl { get; set; } = "";

    // Must match Events:IngestToken on the API.
    public string IngestToken { get; set; } = "";
}
