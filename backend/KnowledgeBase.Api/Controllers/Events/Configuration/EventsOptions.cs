namespace KnowledgeBase.Api.Controllers.Events.Configuration;

public sealed class EventsOptions
{
    public const string SectionName = "Events";

    // Shared with the worker, which posts its change hints to /api/events/ingest with this in a
    // header. That endpoint has no other guard, so an empty token (the default) turns it off.
    public string IngestToken { get; set; } = "";
}
