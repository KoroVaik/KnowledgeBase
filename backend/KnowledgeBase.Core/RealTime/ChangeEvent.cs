namespace KnowledgeBase.Core.RealTime;

/// <summary>
/// A hint that a collection changed, sent to every open browser. Not the data itself - there
/// is no event log, so a client that missed one just re-reads the collection.
/// </summary>
public sealed record ChangeEvent(string Resource, string Action, string? Id = null)
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public string? TraceParent { get; init; } = System.Diagnostics.Activity.Current?.Id;
    public KnowledgeBase.Core.Observability.OperationContext Context { get; init; } = KnowledgeBase.Core.Observability.OperationContext.Current;
}

public static class ChangeResources
{
    public const string Assets = "assets";
    public const string Notes = "notes";

    public const string PhotoAnalysis = "photo-analysis";
}

public static class ChangeActions
{
    public const string Created = "created";

    public const string Updated = "updated";

    public const string Deleted = "deleted";
}
