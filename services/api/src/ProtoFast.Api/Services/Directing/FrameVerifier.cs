using System.Text;
using System.Text.Json;
using ProtoFast.DocumentImport.Screenplay.Models;
using static ProtoFast.Api.Services.Directing.FrameSheets;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// Checks directed frames before they are pictured, so the author doesn't have to move people by
/// hand afterwards. Code checks what it can (everyone has a side, depth and mark; screen direction
/// holds); then two model verifiers, one for consistency and one for positioning, read the frames
/// against each other, the script and the pictures already made. A verifier that fails reports
/// nothing: a check never stops a frame from being directed.
/// </summary>
public sealed class FrameVerifier(ILanguageModelFactory models, DirectorOptions options, ILogger<FrameVerifier> logger)
{
    private const int MaxPerFrame = 4;

    private const string System = """
        You check a director's staging of a screenplay's frames before they are pictured. Each frame
        is one still picture and says where its camera is and where everyone and everything is in it.
        Report only real problems, each in one or two sentences that say what is wrong and what would
        put it right. Never ask for anything the script and the author's instructions don't support,
        and never report taste. Use the frame labels (f1, f2, …) and names given to you. Answer with
        one JSON object in exactly the shape requested and nothing else: no prose, no code fences.
        """;

    private const string ConsistencyRubric = """
        Check each frame marked (check) for consistency with the frames around it, the looks and the
        pictures shown. Problems: someone or something on another mark than in the frame before when
        nothing in the rows between moves them; a prop that appears, goes or moves with nobody moving
        it; someone carrying something other than what Looks says, or nothing; a wardrobe or
        appearance other than Looks says; someone on screen who has left, or missing where the rows
        have them in the picture; someone who arrives without coming through a way into the place;
        light or time of day that changes within the place; a picture already made that shows someone
        or something elsewhere than the staging, with nothing between to move them.
        """;

    private const string PositioningRubric = """
        Check each frame marked (check) for positioning: whether everyone and everything is where the
        rows, the place and the camera need them. Problems: someone the rows have at, on or going
        through part of the place (a door, the counter, a bed) whose mark is elsewhere; a mark the
        place doesn't have, by its layout, its parts or its pictures; a screen side or depth that
        can't be where that mark falls from where the camera stands; two people on one spot; people
        who talk to each other but can't see each other; the people the rows are about hidden behind
        extras or props, or pushed to an edge while extras hold the middle; a "has now" that puts
        someone or something on another side, depth or spot than the staging does; someone going
        through a door seen from the side they don't come into.
        """;

    /// <param name="ordered">Every frame of the scene in order, the checked ones among them.</param>
    /// <param name="pictures">Pictures already made that show the place and the frames around these, each captioned.</param>
    /// <param name="instructions">The author's instructions for a single frame, which no finding may undo.</param>
    public async Task<List<StagingFinding>> VerifyAsync(
        SceneBrief brief, DirectionDraft draft, List<FrameRecord> ordered, IReadOnlyList<FrameRecord> checkedFrames,
        IReadOnlyList<ModelImage> pictures, string instructions, CancellationToken ct)
    {
        var findings = new List<StagingFinding>();
        foreach (var frame in checkedFrames)
        {
            findings.AddRange(StagingChecks.Gaps(brief, draft, frame)
                .Concat(StagingChecks.DirectionIssues(brief, draft, ordered, checkedFrames, frame))
                .Select(issue => new StagingFinding(frame, FrameChecks.Positioning, issue)));
        }

        var listing = new StringBuilder();
        for (var i = 0; i < ordered.Count; i++)
        {
            listing.Append('f').Append(i + 1).Append(checkedFrames.Contains(ordered[i]) ? " (check) " : " ")
                .AppendLine(Staging(brief, draft, ordered[i]));
        }

        var asked = await Task.WhenAll(
            AskAsync(FrameChecks.Consistency, ConsistencyRubric, brief, draft, ordered, checkedFrames, listing.ToString(), pictures, instructions, ct),
            AskAsync(FrameChecks.Positioning, PositioningRubric, brief, draft, ordered, checkedFrames, listing.ToString(), pictures, instructions, ct));
        findings.AddRange(asked.SelectMany(f => f));
        return findings;
    }

    private async Task<List<StagingFinding>> AskAsync(
        string check, string rubric, SceneBrief brief, DirectionDraft draft, IReadOnlyList<FrameRecord> ordered,
        IReadOnlyList<FrameRecord> checkedFrames, string listing, IReadOnlyList<ModelImage> pictures, string instructions,
        CancellationToken ct)
    {
        var prompt = $$"""
            {{rubric}}
            The director staged them by these rules:
            {{StagingRules.Staging}}
            {{StagingRules.Continuity}}
            Frames, in order:
            {{listing}}
            Cast:
            {{brief.CastSheet()}}
            Looks:
            {{brief.LookSheet(draft.Appearances, draft.LookChanges)}}
            Extras:
            {{SceneBrief.ExtraSheet(brief.Extras)}}
            Props:
            {{brief.PropSheet()}}
            Places:
            {{brief.LocationSheet(withLayout: true)}}
            Scene {{brief.Heading()}}:
            {{brief.Script()}}
            {{(instructions.Length == 0 ? "" : $"The author's instructions for the frame, which it must follow and no problem may undo:\n{instructions}")}}
            Reply, with an empty list when nothing is wrong: {"findings":[{"frame":"f2","issue":"…"}]}
            """;

        try
        {
            using var json = await AskAsync(pictures, prompt, ct);
            var findings = new List<StagingFinding>();
            foreach (var answer in ModelJson.Array(json.RootElement, "findings"))
            {
                var label = ModelJson.Str(answer, "frame")?.Trim().TrimStart('f', 'F');
                var issue = Performances.Collapse(ModelJson.Str(answer, "issue") ?? "");
                if (!int.TryParse(label, out var number) || number < 1 || number > ordered.Count || issue.Length == 0)
                {
                    continue;
                }

                var frame = ordered[number - 1];
                if (checkedFrames.Contains(frame) && findings.Count(f => f.Frame == frame) < MaxPerFrame)
                {
                    findings.Add(new StagingFinding(frame, check, issue.Length <= DirectionValidator.BeatMaxLength ? issue : issue[..DirectionValidator.BeatMaxLength]));
                }
            }

            return findings;
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "The {Check} verifier failed; the frames go unchecked for it", check);
            return [];
        }
    }

    /// <summary>One retry with a nudge, as the director's own asks do.</summary>
    private async Task<JsonDocument> AskAsync(IReadOnlyList<ModelImage> pictures, string prompt, CancellationToken ct)
    {
        var model = models.For(options.VisionModelClass, options.VisionProvider);
        var reply = await model.CompleteAsync(System, pictures, prompt, ct);
        try
        {
            return ModelJson.Parse(reply.Text);
        }
        catch (ModelOutputException first)
        {
            logger.LogWarning("{Model} answered outside the requested shape ({Reason}); asking again", model.ModelId, first.Message);
            var again = await model.CompleteAsync(
                System, pictures, prompt + "\n\nYour previous answer was not a JSON object in the requested shape. Reply with the JSON object only.", ct);
            return ModelJson.Parse(again.Text);
        }
    }
}
