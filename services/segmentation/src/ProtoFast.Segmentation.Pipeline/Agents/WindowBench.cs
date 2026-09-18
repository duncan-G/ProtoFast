using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Windowing;
using ProtoFast.Segmentation.Pipeline.Executors;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>What the bench produced: the subtrees the materializer needs, and the digest the orchestrator sees.</summary>
public sealed record BenchResult(
    IReadOnlyDictionary<int, SectionNode> Subtrees,
    IReadOnlyList<ParagraphEdit> Edits,
    BenchDigest Digest,
    string? ModelKey);

/// <summary>
/// The bench of window agents (orchestrator plan §4.1). A plain class with no model of its own: it
/// fans out over the windows in bounded batches and returns <em>one</em> digest.
///
/// <para>The digest is outlines, never subtrees. Those stay in S3 and are addressed by
/// <c>(window, node)</c>, which is what makes the round cap a real bound on spend — an
/// orchestrator round costs a few hundred lines of titles regardless of how long the document
/// is.</para>
///
/// <para>Artifact reuse is the same shape as <c>LabelExecutor.LabelWindowsAsync</c>: a window
/// whose artifact already carries this idempotency key is free, so a resumed run pays only for
/// the windows it had not finished.</para>
/// </summary>
public sealed class WindowBench(
    StructureWindowerAgent windower,
    RunArtifacts artifacts,
    PhaseGate gate,
    PromptAssets assets,
    IOptions<PipelineOptions> options,
    ILogger<WindowBench> logger)
{
    private readonly StructureOptions _structure = options.Value.Structure;

    public async Task<BenchResult> StructureAllAsync(
        IReadOnlyList<StructureWindow> windows,
        StructureContext context,
        Func<string, CancellationToken, Task>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(context);

        var promptVersion = assets.VersionFor(AgentRole.StructureWindower);
        var results = new Dictionary<int, WindowStructureResult>();

        foreach (var batch in windows.Chunk(Math.Max(1, _structure.FanOutBatchSize)))
        {
            var tasks = batch.Select(window => StructureOneAsync(window, context, promptVersion, ct));

            foreach (var result in await Task.WhenAll(tasks))
            {
                results[result.WindowIndex] = result;
            }

            if (progress is not null)
            {
                await progress($"{results.Count} of {windows.Count} structure windows done", ct);
            }
        }

        var ordered = results.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();

        return new BenchResult(
            ordered.ToDictionary(r => r.WindowIndex, r => r.Root),
            [.. ordered.SelectMany(r => r.Edits)],
            new BenchDigest([.. ordered.Select(r => Outline(r, windows))]),
            ordered.Select(r => r.ModelKey).FirstOrDefault(k => k is not null));
    }

    /// <summary>
    /// Puts the orchestrator's questions back to the windows that can answer them, in parallel
    /// again. Each window is asked once with all of its questions — asking twice would double the
    /// cost of the round for no more information.
    /// </summary>
    public async Task<IReadOnlyList<FollowUpAnswer>> AnswerAsync(
        IReadOnlyList<FollowUp> followUps,
        IReadOnlyList<StructureWindow> windows,
        BenchDigest digest,
        StructureContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(followUps);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(digest);

        var outlines = digest.Windows.ToDictionary(w => w.WindowIndex);

        var byWindow = followUps
            .Take(Math.Max(0, _structure.MaxFollowUpsPerRound))
            .Select(f => (FollowUp: f, Window: NodeRefs.TryParse(f.Node, out var index, out _) ? index : -1))
            .Where(x => x.Window >= 0 && outlines.ContainsKey(x.Window))
            .GroupBy(x => x.Window)
            .ToList();

        var answers = new List<FollowUpAnswer>();

        foreach (var batch in byWindow.Chunk(Math.Max(1, _structure.FanOutBatchSize)))
        {
            var tasks = batch.Select(group => windower.AnswerAsync(
                windows.First(w => w.WindowIndex == group.Key),
                outlines[group.Key],
                [.. group.Select(x => x.FollowUp)],
                context,
                ct));

            foreach (var batchAnswers in await Task.WhenAll(tasks))
            {
                answers.AddRange(batchAnswers);
            }
        }

        return answers;
    }

    private async Task<WindowStructureResult> StructureOneAsync(
        StructureWindow window,
        StructureContext context,
        string promptVersion,
        CancellationToken ct)
    {
        var idempotencyKey = IdempotencyKeys.StructureWindow(context.RunId, window.WindowIndex, promptVersion);
        var artifactKey = ArtifactKeys.StructureWindow(context.RunId, window.WindowIndex);

        if (await gate.AlreadyDoneAsync(artifactKey, idempotencyKey, ct) is not null
            && await artifacts.ReadStructureWindowAsync(context.RunId, window.WindowIndex, ct) is { } cached)
        {
            logger.LogDebug(
                "Run {RunId}: reusing structure window {Window}", context.RunId, window.WindowIndex);

            return new WindowStructureResult(
                cached.WindowIndex, cached.Root, cached.Edits, cached.OpenQuestions, cached.ModelKey);
        }

        var result = await windower.StructureAsync(window, context, ct);

        await artifacts.WriteStructureWindowAsync(
            context.RunId,
            new StructureWindowArtifact(
                result.WindowIndex, result.Root, result.Edits, result.OpenQuestions, result.ModelKey),
            idempotencyKey,
            ct);

        return result;
    }

    /// <summary>
    /// Flattens one window's subtree into the rows the orchestrator reasons over. The window's own
    /// synthetic root is left out: it is a wrapper its agent had to return, and a plan that named
    /// it would place the whole window as one untitled section.
    /// </summary>
    private static WindowOutline Outline(WindowStructureResult result, IReadOnlyList<StructureWindow> windows)
    {
        var window = windows.FirstOrDefault(w => w.WindowIndex == result.WindowIndex);
        var nodes = new List<OutlineNode>();
        var index = 0;

        Walk(result.Root, depth: 0);

        return new WindowOutline(
            result.WindowIndex,
            window?.FirstEntryId ?? string.Empty,
            window?.LastEntryId ?? string.Empty,
            nodes,
            result.OpenQuestions);

        void Walk(SectionNode node, int depth)
        {
            var reference = NodeRefs.For(result.WindowIndex, index++);

            if (depth > 0)
            {
                nodes.Add(new OutlineNode(
                    reference,
                    depth,
                    node.Title,
                    node.TitleInferred,
                    node.HeadingLineId,
                    node.ParagraphIds.Count,
                    node.Descend().Count() - 1));
            }

            foreach (var child in node.Children)
            {
                Walk(child, depth + 1);
            }
        }
    }
}
