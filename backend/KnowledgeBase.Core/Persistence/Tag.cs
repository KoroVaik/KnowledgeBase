namespace KnowledgeBase.Core.Persistence;

public sealed class Tag
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    // The user has vouched for this tag. Pipeline-invented tags land false and stay visible,
    // just flagged for review.
    public required bool Confirmed { get; set; }

    // The tag (confirmed, or itself still unconfirmed) the pipeline judged closest in meaning -
    // the review UI's "merge into" offer. Similarity is the model's call, never string distance.
    public string? SuggestedMergeIntoId { get; set; }

    // How sure the model was about SuggestedMergeIntoId - null when there is no suggestion.
    public SuggestionConfidence? SuggestedMergeConfidence { get; set; }
}
