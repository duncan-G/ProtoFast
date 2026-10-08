using System.Text.Json;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

/// <summary>
/// Judges the one stage every import ends in. Structure is a hard failure; a name the library does
/// not know, or a character kind the vocabulary does not declare, only degrades, since the writer fills
/// them in.
/// </summary>
public sealed class StoryDraftVerifier(IArtifactStore artifacts) : IVerifier
{
    public const string VerifierId = "story-draft-json";

    private static readonly IReadOnlyList<string> DefaultKinds = ["Human", "Robot", "Animal", "Creature", "Voice"];

    private static readonly IReadOnlySet<string> Types =
        new HashSet<string>(["Heading", "Action", "Description", "Narration", "Dialogue", "Transition"], StringComparer.OrdinalIgnoreCase);

    public string Id => VerifierId;

    public bool IsDeterministic => true;

    public async Task<VerifierResult> VerifyAsync(StageRequest request, StageResult result, CancellationToken ct)
    {
        StoryDraft story;
        try
        {
            story = StoryJson.Deserialize<StoryDraft>(await ArtifactText.ReadAsync(artifacts, result.Output, ct));
        }
        catch (JsonException e)
        {
            return new VerifierResult(Id, Verdict.Fail, "The output is not a story document.", [new Finding("$", e.Message)]);
        }

        var characters = story.Characters ?? [];
        var locations = story.Locations ?? [];
        var scenes = (story.Containers ?? []).SelectMany(c => c.Scenes ?? []).ToList();

        var findings = new List<Finding>();
        if (string.IsNullOrWhiteSpace(story.Title))
        {
            findings.Add(new Finding("$.title", "A title is required."));
        }

        if (characters.Count == 0)
        {
            findings.Add(new Finding("$.characters", "At least one character is required."));
        }

        findings.AddRange(characters
            .Where(c => string.IsNullOrWhiteSpace(c.Name))
            .Select(_ => new Finding("$.characters[*].name", "A character has no name.")));
        findings.AddRange(Duplicates(characters.Select(c => c.Name), "$.characters"));
        findings.AddRange(Duplicates(locations.Select(l => l.Name), "$.locations"));
        findings.AddRange(Duplicates((story.Props ?? []).Select(p => p.Name), "$.props"));

        if (scenes.Count == 0)
        {
            findings.Add(new Finding("$.containers", "At least one scene is required."));
        }

        foreach (var (scene, index) in scenes.Select((s, i) => (s, i)))
        {
            var elements = scene.Elements ?? [];
            if (elements.Count == 0 || !string.Equals(elements[0].Type, "Heading", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new Finding($"$..scenes[{index}].elements[0]", $"Scene '{scene.Title}' must open with a Heading."));
            }

            foreach (var element in elements)
            {
                if (!Types.Contains(element.Type ?? ""))
                {
                    findings.Add(new Finding($"$..scenes[{index}]", $"'{element.Type}' is not an element type."));
                }
                else if (!IsHeadingOrTransition(element.Type!) && string.IsNullOrWhiteSpace(element.Text))
                {
                    findings.Add(new Finding($"$..scenes[{index}]", $"A {element.Type} element has no text."));
                }
            }
        }

        if (findings.Count > 0)
        {
            return new VerifierResult(Id, Verdict.Fail, "The story is malformed.", findings);
        }

        var characterNames = characters.Select(c => c.Name).ToHashSet(LibraryNameComparer.Instance);
        var locationNames = locations.Select(l => l.Name).ToHashSet(LibraryNameComparer.Instance);
        var kinds = DefaultKinds
            .Concat((story.Vocabulary?.CharacterKinds ?? []).Select(k => k.Label?.Trim() ?? ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var degraded = characters
            .Where(c => !string.IsNullOrWhiteSpace(c.Kind) && !kinds.Contains(c.Kind.Trim()))
            .Select(c => new Finding(
                $"$.characters[{c.Name}].kind",
                $"'{c.Kind}' is neither a default character kind nor in vocabulary.characterKinds, so it gets a circle avatar."))
            .Concat(scenes.SelectMany(s => s.Elements ?? [])
                .Select(e => e.Speaker is { } speaker && !characterNames.Contains(speaker)
                    ? new Finding("$..speaker", KeepsExtension(speaker)
                        ? $"'{speaker}' keeps its cue extension; the name goes in speaker and the extension in extension."
                        : $"'{speaker}' is not a library character.")
                    : e.Location is { } location && !locationNames.Contains(location)
                        ? new Finding("$..location", $"'{location}' is not a library location.")
                        : null)
                .OfType<Finding>())
            .DistinctBy(f => f.Message)
            .ToList();

        return degraded.Count > 0
            ? new VerifierResult(Id, Verdict.Degraded, "Some names or kinds are filled in by default.", degraded)
            : new VerifierResult(Id, Verdict.Pass, "The story is complete.", []);
    }

    private static IEnumerable<Finding> Duplicates(IEnumerable<string?> names, string path) =>
        names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .GroupBy(n => n!.Trim(), LibraryNameComparer.Instance)
            .Where(g => g.Count() > 1)
            .Select(g => new Finding(path, $"'{g.Key}' appears more than once."));

    private static bool KeepsExtension(string speaker) =>
        speaker.TrimEnd().EndsWith(')') && speaker.IndexOf('(') > 0;

    private static bool IsHeadingOrTransition(string type) =>
        type.Equals("Heading", StringComparison.OrdinalIgnoreCase) || type.Equals("Transition", StringComparison.OrdinalIgnoreCase);
}
