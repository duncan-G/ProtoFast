using ProtoFast.DocumentImport.Screenplay.Drafts;

namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <summary>What keeps a written documentary from saving as a well-formed story.</summary>
public static class DraftProblems
{
    private static readonly HashSet<string> Types = new(["Heading", "Narration", "Action", "Dialogue", "Transition"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Find(StoryDraft draft)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            problems.Add("The story has no title.");
        }

        var scenes = (draft.Containers ?? []).SelectMany(c => c.Scenes ?? []).ToList();
        if (scenes.Count == 0)
        {
            problems.Add("The story has no scenes.");
        }

        if ((draft.Characters ?? []).Any(c => IsNarrator(c.Name))
            || scenes.SelectMany(s => s.Elements ?? []).Any(e => Is(e, "Dialogue") && IsNarrator(e.Speaker)))
        {
            problems.Add("The narrator is a character; write narration as Narration elements and drop the Narrator character.");
        }

        foreach (var (scene, index) in scenes.Select((s, i) => (s, i + 1)))
        {
            var name = $"Scene {index} ({scene.Title})";
            var elements = scene.Elements ?? [];
            if (elements.Count == 0 || !Is(elements[0], "Heading"))
            {
                problems.Add($"{name} does not open with a Heading.");
            }

            if (!elements.Any(e => Is(e, "Narration") && !string.IsNullOrWhiteSpace(e.Text)))
            {
                problems.Add($"{name} has no narration; the narrator speaks in every scene.");
            }

            foreach (var (element, position) in elements.Select((e, i) => (e, i + 1)))
            {
                if (!Types.Contains(element.Type ?? ""))
                {
                    problems.Add($"{name}, element {position}: type {element.Type} is not allowed; use Narration, Action, Dialogue, Heading or Transition.");
                }
                else if (Is(element, "Heading") && string.IsNullOrWhiteSpace(element.Location))
                {
                    problems.Add($"{name}, element {position}: the Heading names no location.");
                }
                else if (Is(element, "Dialogue") && (string.IsNullOrWhiteSpace(element.Speaker) || string.IsNullOrWhiteSpace(element.Text)))
                {
                    problems.Add($"{name}, element {position}: Dialogue needs a speaker and text.");
                }
                else if ((Is(element, "Action") || Is(element, "Narration")) && string.IsNullOrWhiteSpace(element.Text))
                {
                    problems.Add($"{name}, element {position}: {element.Type} needs text.");
                }
            }
        }

        return problems;
    }

    private static bool Is(SceneElementDraft element, string type) => string.Equals(element.Type, type, StringComparison.OrdinalIgnoreCase);

    private static bool IsNarrator(string? name) => string.Equals(name?.Trim(), "Narrator", StringComparison.OrdinalIgnoreCase);
}
