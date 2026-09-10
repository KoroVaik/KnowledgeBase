namespace KnowledgeBase.Api.Controllers.Notes.Contracts;

/// <summary>
/// What one [[title]] points at now. Computed per read - the body keeps the plain [[title]]
/// so it stays exportable to Obsidian.
/// </summary>
/// <param name="Title">The text between the brackets, as written in the body.</param>
/// <param name="State">resolved: a live note. deleted: it is in the bin. missing: no such note.</param>
/// <param name="TargetId">The note pointed at, for resolved and deleted. Null for missing.</param>
public sealed record NoteLinkStateResponse(string Title, string State, string? TargetId);

public static class NoteLinkStates
{
    public const string Resolved = "resolved";

    public const string Deleted = "deleted";

    public const string Missing = "missing";
}
