namespace KnowledgeBase.Api.Controllers.Tags;

// A soft review hint, not a filter: the model sometimes returns a whole phrase ("Futuristic Car and
// Aircraft in Hangar") as one tag. A false positive ("Made in Italy") only costs one confirm, so
// plain string shape is acceptable here, unlike for merge similarity (see docs/ai-pipeline.md).
// A bare word count was too blunt - three-word tags ("Home Fermentation Tips") are common.
internal static class TagNameShape
{
    private const int CombinedFromWords = 5;

    private static readonly char[] Separators = [',', '/', '&', '+', ';'];

    private static readonly HashSet<string> Connectors =
        new(["and", "or", "with", "vs", "in", "on"], StringComparer.OrdinalIgnoreCase);

    public static bool PossiblyCombined(string name, bool confirmed)
    {
        if (confirmed)
        {
            return false;
        }

        if (name.IndexOfAny(Separators) >= 0)
        {
            return true;
        }

        // Only tokens with a letter count - a "(2)" suffix is not a word.
        var words = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Any(char.IsLetter))
            .ToList();

        return words.Count >= CombinedFromWords || words.Any(Connectors.Contains);
    }
}
