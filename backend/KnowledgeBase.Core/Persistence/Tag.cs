namespace KnowledgeBase.Core.Persistence;

public sealed class Tag
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    // The user has vouched for this tag. Pipeline-invented tags land false and stay visible,
    // just flagged for review.
    public required bool Confirmed { get; set; }

    // The confirmed tag the pipeline judged closest in meaning when it invented this one -
    // the review UI's "merge into" offer. Similarity is the model's call, never string distance.
    public string? SuggestedMergeIntoId { get; set; }
}
