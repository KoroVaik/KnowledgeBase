namespace KnowledgeBase.Api.Controllers.Synthesis.Contracts;

/// <summary>Ask for the Source notes carrying one tag to be merged into a Synthesis note.</summary>
public sealed record SynthesiseTagRequest(string Tag);
