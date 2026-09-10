namespace KnowledgeBase.Core.Persistence;

public sealed class Tag
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    // The user has vouched for this tag. Pipeline-invented tags land false and stay visible,
    // just flagged for review.
    public required bool Confirmed { get; set; }
}
