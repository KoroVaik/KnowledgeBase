namespace KnowledgeBase.Core.RealTime;

/// <summary>
/// A hint that a collection changed, sent to every open browser. Not the data itself - there
/// is no event log, so a client that missed one just re-reads the collection.
/// </summary>
public sealed record ChangeEvent(string Resource, string Action, string? Id = null);

public static class ChangeResources
{
    public const string Assets = "assets";
    public const string Notes = "notes";
}

public static class ChangeActions
{
    public const string Created = "created";

    public const string Updated = "updated";

    public const string Deleted = "deleted";
}
