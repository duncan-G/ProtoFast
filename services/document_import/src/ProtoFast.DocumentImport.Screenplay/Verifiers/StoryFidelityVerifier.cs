using System.Text.Json;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

/// <summary>
/// Judges that the story is the manuscript's own text, split into elements, not a rewrite of it: most
/// of the story's words must come from the manuscript and most of the manuscript must be in the story.
/// Only the prose fields count; library descriptions and names are the agent's to write.
/// </summary>
public sealed class StoryFidelityVerifier(IArtifactStore artifacts, IRunLedger ledger) : IVerifier
{
    public const string VerifierId = "story-fidelity";

    // Edge repairs after a split and dropped dialogue tags cost a few words; a paraphrase costs most of them.
    public const double MinKept = 0.8;

    // Headings, speaker cues and front matter are not story text, so the manuscript is never fully covered.
    public const double MinCovered = 0.6;

    private const int MaxFindings = 10;
    private const int MinMissingWords = 20;

    public string Id => VerifierId;

    public bool IsDeterministic => true;

    public async Task<VerifierResult> VerifyAsync(StageRequest request, StageResult result, CancellationToken ct)
    {
        if (await ManuscriptAsync(request, ct) is not { } manuscript)
        {
            return new VerifierResult(Id, Verdict.Fail, "The run's input document could not be found, so fidelity cannot be judged.", []);
        }

        StoryDraft story;
        try
        {
            story = StoryJson.Deserialize<StoryDraft>(await ArtifactText.ReadAsync(artifacts, result.Output, ct));
        }
        catch (JsonException e)
        {
            return new VerifierResult(Id, Verdict.Fail, "The output is not a story document.", [new Finding("$", e.Message)]);
        }

        var overlap = TextOverlap.Measure(await ArtifactText.ReadAsync(artifacts, manuscript, ct), Units(story).ToList());
        var findings = new List<Finding>();
        if (overlap.Kept < MinKept)
        {
            findings.AddRange(overlap.Units
                .Where(u => u.Kept < u.Words)
                .OrderByDescending(u => u.Words - u.Kept)
                .Take(MaxFindings)
                .Select(u => new Finding(u.Path, $"{u.Words - u.Kept} of {u.Words} words are not the manuscript's: \"{u.Snippet}\"")));
        }

        if (overlap.Covered < MinCovered)
        {
            findings.AddRange(overlap.Missing
                .Where(m => m.Words >= MinMissingWords)
                .Take(MaxFindings / 2)
                .Select(m => new Finding(ArtifactRef.InputStageId, $"{m.Words} manuscript words are not in the story: \"{m.Snippet}\"")));
        }

        var reason = $"{overlap.Kept * 100:0}% of the story's words are the manuscript's and {overlap.Covered * 100:0}% of the manuscript is in the story";
        return overlap.Kept < MinKept || overlap.Covered < MinCovered
            ? new VerifierResult(Id, Verdict.Fail, $"{reason}; at least {MinKept * 100:0}% and {MinCovered * 100:0}% are required. Keep the manuscript's wording, split into elements, and drop only what is not story text.", findings)
            : new VerifierResult(Id, Verdict.Pass, $"{reason}.", []);
    }

    /// <summary>The run input, whether this stage read it directly or an earlier stage did.</summary>
    private async Task<ArtifactRef?> ManuscriptAsync(StageRequest request, CancellationToken ct)
    {
        var direct = request.Inputs.FirstOrDefault(i => i.StageId == ArtifactRef.InputStageId);
        if (!direct.IsNone)
        {
            return direct;
        }

        var recorded = (await ledger.SummariseAsync(request.RunId, ct)).Stages
            .SelectMany(s => s.Inputs)
            .FirstOrDefault(i => i.StageId == ArtifactRef.InputStageId);
        return recorded.IsNone ? null : recorded;
    }

    private static IEnumerable<TextUnit> Units(StoryDraft story)
    {
        if (!string.IsNullOrWhiteSpace(story.Title))
        {
            yield return new TextUnit("$.title", story.Title);
        }

        foreach (var (container, c) in (story.Containers ?? []).Select((x, i) => (x, i)))
        {
            foreach (var (scene, s) in (container.Scenes ?? []).Select((x, i) => (x, i)))
            {
                foreach (var (element, e) in (scene.Elements ?? []).Select((x, i) => (x, i)))
                {
                    var path = $"$.containers[{c}].scenes[{s}].elements[{e}]";
                    if (!string.IsNullOrWhiteSpace(element.Text))
                    {
                        yield return new TextUnit($"{path}.text", element.Text);
                    }

                    if (!string.IsNullOrWhiteSpace(element.Parenthetical))
                    {
                        yield return new TextUnit($"{path}.parenthetical", element.Parenthetical);
                    }
                }
            }
        }
    }
}
