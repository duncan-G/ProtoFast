using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

/// <summary>
/// Fails a skill that names a work the family has imported: its title, or one of its characters.
/// A character name counts only when its manuscript never writes it as an ordinary lowercase word,
/// so NURSE or MAN #2 may appear in a skill and MOLLY may not.
/// </summary>
public sealed partial class SkillNamesVerifier(IArtifactStore artifacts, IRunLedger ledger, EngineOptions options) : ISkillVerifier
{
    public const string VerifierId = "skill-names";

    private const int MaxListed = 5;

    public string Id => VerifierId;

    public bool IsDeterministic => true;

    public async Task<VerifierResult> VerifyAsync(SkillReview review, CancellationToken ct)
    {
        var names = await NamesAsync(review, ct);
        var findings = new List<Finding>();
        var named = new List<string>();
        foreach (var (path, text) in Texts(review))
        {
            var words = Normalize(text);
            var found = names.Where(n => words.Contains($" {n.Key} ", StringComparison.Ordinal)).Select(n => n.Value).ToList();
            if (found.Count > 0)
            {
                findings.Add(new Finding(path, $"names {Listed(found)}"));
                named.AddRange(found);
            }
        }

        if (findings.Count == 0)
        {
            return new VerifierResult(Id, Verdict.Pass, "The skill names no imported work's title or characters.", []);
        }

        return new VerifierResult(
            Id, Verdict.Fail,
            $"The skill is written for particular manuscripts: it names {Listed(named.Distinct().ToList())}. " +
            "A skill serves every document of the family, so find such names in the document at run time (by pattern, " +
            "or as labels a model passes in) instead of writing them in, and remove or generalize scripts made for one document.",
            findings);
    }

    private static string Listed(IReadOnlyList<string> names) =>
        string.Join("; ", names.Take(MaxListed)) + (names.Count > MaxListed ? $" and {names.Count - MaxListed} more" : "");

    private static IEnumerable<(string Path, string Text)> Texts(SkillReview review)
    {
        yield return ("description", review.Skill.Description);
        yield return ("instructions", review.Skill.Instructions);
        foreach (var script in review.Skill.Scripts)
        {
            yield return ($"scripts/{script.Name}",
                $"{script.Name}\n{script.Description}\n{review.Sources.GetValueOrDefault(script.Name)}");
        }
    }

    /// <summary>Normalized name to how a finding describes it, over this run's story and the family's recent ones.</summary>
    private async Task<IReadOnlyDictionary<string, string>> NamesAsync(SkillReview review, CancellationToken ct)
    {
        var runs = (await ledger.RecentAsync(review.DocumentSignature.Family, RunMode.Discovery, options.ContextRuns, ct))
            .Prepend(await ledger.SummariseAsync(review.RunId, ct))
            .DistinctBy(r => r.RunId);

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var run in runs)
        {
            var story = run.Stages.LastOrDefault(s => s.StageId == StoryStages.StoryStage && s.Passed && !s.IsShadow);
            var manuscript = run.RunId == review.RunId
                ? review.Input
                : run.Stages.SelectMany(s => s.Inputs).FirstOrDefault(i => i.StageId == ArtifactRef.InputStageId);
            if (story is null || manuscript.IsNone)
            {
                continue;
            }

            StoryDraft draft;
            string text;
            try
            {
                draft = StoryJson.Deserialize<StoryDraft>(await ArtifactText.ReadAsync(artifacts, story.Output, ct));
                text = await ArtifactText.ReadAsync(artifacts, manuscript, ct);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException)
            {
                continue;
            }

            var title = Normalize(draft.Title ?? "").Trim();
            var work = title.Length > 0 ? $" of \"{draft.Title}\"" : "";
            var ordinary = OrdinaryWord().Matches(text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
            if (title.Contains(' ') || IsDistinctive(title, ordinary))
            {
                names.TryAdd(title, $"the title \"{draft.Title}\"");
            }

            foreach (var character in draft.Characters ?? [])
            {
                var name = Normalize(character.Name ?? "").Trim();
                if (IsDistinctive(name, ordinary))
                {
                    names.TryAdd(name, $"{character.Name}, a character{work}");
                }
            }
        }

        return names;
    }

    private static bool IsDistinctive(string name, HashSet<string> ordinary) =>
        name.Split(' ').Any(word => word.Length >= 3 && word.All(char.IsLetter) && !ordinary.Contains(word));

    /// <summary>Lowercase words between spaces, so a name matches whole words whatever its case and punctuation.</summary>
    private static string Normalize(string text)
    {
        var normalized = new StringBuilder(text.Length + 2).Append(' ');
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                normalized.Append(char.ToLowerInvariant(c));
            }
            else if (normalized[^1] != ' ')
            {
                normalized.Append(' ');
            }
        }

        return normalized[^1] == ' ' ? normalized.ToString() : normalized.Append(' ').ToString();
    }

    [GeneratedRegex(@"(?<!\p{L})\p{Ll}+(?!\p{L})")]
    private static partial Regex OrdinaryWord();
}
