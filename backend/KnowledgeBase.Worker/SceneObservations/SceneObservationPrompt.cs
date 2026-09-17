using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;

namespace KnowledgeBase.Worker.SceneObservations;

public static class SceneObservationPrompt
{
    public static AiTask TaskFor(AnalysisImage image, IReadOnlyList<Person> people, IReadOnlyList<Location> locations) =>
        new(System, UserPrompt(people, locations), SceneObservationDraft.Schema, image);

    private const string System =
        """
        You extract cautious, structured visual observations from one personal-archive photograph.
        Return only JSON matching the schema.
        Record only what is visibly supported by the image. Return [] when there is nothing useful.
        Kinds:
        - Action: a visible activity.
        - Interaction: a visible relationship or interaction between two people.
        - Object: a salient visible object.
        - Text: clearly readable visible text; quote it exactly in description.
        - Mood: an observable visual cue such as smiling or a tense pose, never an inner state.
        Do not identify a person unless their name appears exactly in Confirmed people. Never invent
        a person, place, date, event, relationship, motive, or hidden context. For a person you do
        not name, leave person-name fields empty. Evidence must name the visible cue supporting the
        observation. Confidence is a cautious 0 to 1 ranking, not a probability or fact.
        """;

    private static string UserPrompt(IReadOnlyList<Person> people, IReadOnlyList<Location> locations)
    {
        var peopleContext = people.Count == 0 ? "(none)" : string.Join("\n", people.Select(person => $"- {person.Name}"));
        var locationContext = locations.Count == 0 ? "(none)" : string.Join("\n", locations.Select(location => $"- {location.Name} ({location.Kind})"));
        return $"""
            Confirmed people visible in this photo (names may be used, but are not required):
            {peopleContext}

            Confirmed location context for this photo:
            {locationContext}

            Analyze the attached image.
            """;
    }
}
