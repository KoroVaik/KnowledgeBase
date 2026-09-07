namespace Backend.Infrastructure.RealTime;

/// <summary>
/// A hint that a collection changed, sent to every browser with the app open.
/// </summary>
/// <remarks>
/// Deliberately not the changed data itself. Nothing keeps a log of past events, so a client
/// that was disconnected for a second cannot be handed what it missed - it re-reads the whole
/// collection instead. That makes a lost event harmless and keeps this contract stable.
/// </remarks>
public sealed record ChangeEvent(string Resource, string Action, string? Id = null);

public static class ChangeResources
{
    public const string Assets = "assets";
}

public static class ChangeActions
{
    public const string Created = "created";
    public const string Deleted = "deleted";
}
