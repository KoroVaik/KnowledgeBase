namespace KnowledgeBase.Core.Persistence;

public sealed class UserAccount
{
    // Seeded by the migration. Until registration exists, every sign-in path resolves here.
    public const string OwnerId = "00000000000000000000000000000001";

    public required string Id { get; init; }

    // Stored lower-cased; how a Google sign-in finds its account once there is more than one.
    public string? Email { get; set; }

    public required DateTime CreatedAtUtc { get; init; }
}
