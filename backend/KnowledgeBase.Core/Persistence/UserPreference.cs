namespace KnowledgeBase.Core.Persistence;

/// <summary>One UI setting of one user, e.g. <c>section-collapsed:tags</c> → <c>true</c>.</summary>
public sealed class UserPreference
{
    public required string UserId { get; init; }

    public required string Key { get; init; }

    public required string ValueJson { get; set; }

    public required DateTime UpdatedAtUtc { get; set; }
}
