namespace KnowledgeBase.Core.Persistence;

public sealed class Person
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required DateTime CreatedAtUtc { get; init; }
}
