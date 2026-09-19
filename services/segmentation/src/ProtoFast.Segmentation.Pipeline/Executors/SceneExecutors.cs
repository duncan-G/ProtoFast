using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Classification;
using ProtoFast.Segmentation.Core.Families;
using ProtoFast.Segmentation.Core.Items;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Personas;
using ProtoFast.Segmentation.Core.Scenes;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Pipeline.Agents;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Phase 5 (scene plan §8.5): separate displayable text from metadata, and emit the per-paragraph
/// composition-family evidence.
///
/// <para>It sits <b>before</b> structure inference deliberately: front matter and a table of
/// contents inferred <em>as sections</em> is noise in the tree, and withholding them first makes the
/// tree both smaller and better.</para>
///
/// <para>Phase 5 classifies against the <b>run</b> family rather than a per-section one, and that is
/// the right answer rather than a concession — front matter, a copyright page and a table of
/// contents belong to the volume, not to the works inside it, so an anthology's metadata policy is
/// the anthology's (§6.1).</para>
/// </summary>
public sealed class PresentationExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    PhaseGate gate,
    PresentationClassifierAgent classifier,
    PromptAssets assets,
    SceneContextFactory contexts,
    IOptions<PipelineOptions> options,
    ILogger<PresentationExecutor> logger)
    : Executor<AssembleComplete, PresentationComplete>(ExecutorIds.Presentation, declareCrossRunShareable: true)
{
    public override async ValueTask<PresentationComplete> HandleAsync(
        AssembleComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var key = IdempotencyKeys.Phase(
            message.RunId, PipelinePhase.ClassifyPresentation,
            assets.VersionFor(AgentRole.PresentationClassifier));

        if (await gate.AlreadyDoneAsync(
                message.RunId, PipelinePhase.ClassifyPresentation, key, null, cancellationToken) is { } done
            && await artifacts.ReadPresentationAsync(message.RunId, cancellationToken) is { } cached)
        {
            return new PresentationComplete(
                message.RunId, done, cached.CompositionFamily,
                cached.Presentations.Count(p => !p.IsDisplayable));
        }

        await journal.StartAsync(message.RunId, PipelinePhase.ClassifyPresentation, key, cancellationToken);

        var assembly = await artifacts.ReadAssemblyAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(
                PipelinePhase.ClassifyPresentation, "The assembly artifact is missing.");
        var cleaning = await artifacts.ReadCleaningAsync(message.RunId, cancellationToken);

        // Step 5, and it runs whatever the classifier does: the feature vector is what phase 7
        // attributes to the tree to derive family scopes, and it is deterministic (§6.1).
        var evidence = FamilyEvidenceExtractor.Extract(assembly.Paragraphs);

        var sceneContext = await contexts.CreateAsync(message.RunId, cancellationToken);
        var compositionFamily = FamilyEvidenceExtractor.Confirm(evidence, sceneContext.CompositionFamily);

        // Onto the run row, not only into the artifact: it is what every later phase resolves
        // against as the fallback of the §6.1 lookup, and what the Composition-axis instincts are
        // drawn for.
        await journal.RecordCompositionFamilyAsync(message.RunId, compositionFamily, cancellationToken);

        var artifactLineIds = cleaning?.ArtifactLineIds ?? new HashSet<string>(StringComparer.Ordinal);
        var artifactParagraphs = assembly.Paragraphs
            .Where(p => p.LineIds.Count > 0 && p.LineIds.All(artifactLineIds.Contains))
            .Select(p => p.ParagraphId)
            .ToHashSet(StringComparer.Ordinal);

        var candidates = PresentationCandidates.Propose(
            assembly.Paragraphs, artifactParagraphs, options.Value.Presentation);

        var (answers, modelKey) = await classifier.ClassifyAsync(
            candidates,
            assembly.Paragraphs.ToDictionary(p => p.ParagraphId, StringComparer.Ordinal),
            sceneContext with { CompositionFamily = compositionFamily },
            cancellationToken);

        var presentations = PresentationCandidates.EnforceEdges(
            PresentationCandidates.Resolve(assembly.Paragraphs, candidates, answers));

        var integrity = SceneChecks.CheckPresentationIntegrity(
            assembly.Paragraphs, presentations, assembly.Headings);

        if (!integrity.Passed)
        {
            // presentation-integrity is a hard gate, and like text-integrity it is a code defect
            // rather than a model one: a classification withholds a paragraph from the scene stream
            // and changes nothing about the record, so a mismatch means the record was changed.
            await journal.FailAsync(
                message.RunId, PipelinePhase.ClassifyPresentation, integrity.ErrorReport, cancellationToken);

            throw new PipelineFailureException(PipelinePhase.ClassifyPresentation, integrity.ErrorReport);
        }

        var recall = SceneChecks.CheckMetadataRecall(presentations, options.Value.Presentation);
        if (!recall.Passed)
        {
            await journal.NoteAsync(
                message.RunId, PipelinePhase.ClassifyPresentation, recall.ErrorReport, cancellationToken);
        }

        await artifacts.WriteFamilyEvidenceAsync(message.RunId, evidence, key, cancellationToken);

        var artifact = await artifacts.WritePresentationAsync(
            message.RunId,
            new PresentationArtifact(presentations, compositionFamily, modelKey),
            key,
            cancellationToken);

        var metadata = presentations.Count(p => !p.IsDisplayable);

        if (modelKey is not null)
        {
            await journal.PinModelAsync(
                message.RunId, AgentRole.PresentationClassifier, modelKey, cancellationToken);
        }

        logger.LogInformation(
            "Run {RunId}: {Metadata} of {Total} paragraphs withheld from the scene stream; "
            + "composition family '{Family}'.",
            message.RunId, metadata, presentations.Count, compositionFamily);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.ClassifyPresentation, artifact.Key,
            $"{metadata} metadata paragraphs, family '{compositionFamily}'"
            + (modelKey is null ? " (deterministic)" : $" via {modelKey}"),
            cancellationToken);

        return new PresentationComplete(message.RunId, artifact, compositionFamily, metadata);
    }
}

/// <summary>
/// Phase 8 (scene plan §8.6): partition displayable paragraphs into typed items with candidate tags.
///
/// <para>A fan-out rather than an orchestration, and the planner's guarantee is what makes it one:
/// <b>items never cross a paragraph, and each paragraph is committed by exactly one window</b>
/// (§8.2). An orchestrator here would replace a guarantee with an opinion.</para>
/// </summary>
public sealed class ItemExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    PhaseGate gate,
    ItemTyperAgent typer,
    PromptAssets assets,
    SceneContextFactory contexts,
    IOptions<PipelineOptions> options,
    ILogger<ItemExecutor> logger)
    : Executor<ValidationComplete, ItemsComplete>(ExecutorIds.Items, declareCrossRunShareable: true)
{
    private readonly ItemOptions _items = options.Value.Items;

    public override async ValueTask<ItemsComplete> HandleAsync(
        ValidationComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var promptVersion = assets.VersionFor(AgentRole.ItemTyper);
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.TypeItems, promptVersion);

        if (await gate.AlreadyDoneAsync(message.RunId, PipelinePhase.TypeItems, key, null, cancellationToken)
            is { } done)
        {
            var cached = await artifacts.ReadItemsAsync(message.RunId, cancellationToken);
            return new ItemsComplete(message.RunId, done, cached.Count, message.Passed);
        }

        await journal.StartAsync(message.RunId, PipelinePhase.TypeItems, key, cancellationToken);

        var assembly = await artifacts.ReadAssemblyAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.TypeItems, "The assembly artifact is missing.");
        var presentation = await artifacts.ReadPresentationAsync(message.RunId, cancellationToken);
        var tree = await artifacts.ReadTreeAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.TypeItems, "The tree artifact is missing.");
        var scopes = await artifacts.ReadFamilyScopesAsync(message.RunId, cancellationToken);

        var sceneContext = await contexts.CreateAsync(message.RunId, cancellationToken) with
        {
            CompositionFamily = presentation?.CompositionFamily ?? CompositionFamily.Unknown,
        };

        var families = new FamilyResolver(tree.Root, scopes, sceneContext.CompositionFamily);

        var displayableIds = presentation is null
            ? assembly.Paragraphs.Select(p => p.ParagraphId).ToHashSet(StringComparer.Ordinal)
            : presentation.Presentations.Where(p => p.IsDisplayable)
                .Select(p => p.ParagraphId).ToHashSet(StringComparer.Ordinal);

        var displayable = assembly.Paragraphs.Where(p => displayableIds.Contains(p.ParagraphId)).ToList();
        var sectionOf = SectionOfParagraph(tree.Root);

        var windows = PlanWindows(displayable);
        var items = new List<SceneItem>();
        string? modelKey = null;

        // Counters are threaded rather than restarted per window, so item and tag ids are unique
        // and monotonic across the document — which is what lets ordinal comparison on an item id
        // stand in for document order everywhere downstream.
        var itemCounter = 0;
        var tagCounter = 0;

        foreach (var window in windows)
        {
            var idempotencyKey = IdempotencyKeys.ItemWindow(message.RunId, window.WindowIndex, promptVersion);
            var artifactKey = Storage.ArtifactKeys.ItemWindow(message.RunId, window.WindowIndex);

            ItemWindowResult result;

            if (await gate.AlreadyDoneAsync(artifactKey, idempotencyKey, cancellationToken) is not null
                && await artifacts.ReadItemWindowAsync(message.RunId, window.WindowIndex, cancellationToken)
                    is { } cached)
            {
                result = cached;
            }
            else
            {
                var family = families.Resolve(
                    window.Committed.Count > 0
                        ? sectionOf.GetValueOrDefault(window.Committed[0].ParagraphId)
                        : null);

                result = await typer.TypeAsync(
                    window, sceneContext, family, itemCounter, tagCounter, cancellationToken);

                await artifacts.WriteItemWindowAsync(message.RunId, result, idempotencyKey, cancellationToken);
            }

            items.AddRange(result.Items);
            modelKey ??= result.ModelKey;

            itemCounter = items.Count;
            tagCounter = items.Sum(i => i.Tags.Count);

            await journal.NoteAsync(
                message.RunId, PipelinePhase.TypeItems,
                $"{window.WindowIndex + 1} of {windows.Count} item windows done", cancellationToken);
        }

        var report = new ValidationReport(
        [
            SceneChecks.CheckItemCoverage(displayable, items),
            SceneChecks.CheckDisplayIntegrity(displayable, items),
            SceneChecks.CheckTagBounds(items),
        ]);

        if (!report.Passed)
        {
            // All three are properties of the materializer, so a failure here is a defect in code
            // rather than a model error — the same posture text-integrity takes, and for the same
            // reason: repairing it with a model would be asking the wrong party.
            await journal.FailAsync(
                message.RunId, PipelinePhase.TypeItems, report.ErrorReport, cancellationToken);

            throw new PipelineFailureException(PipelinePhase.TypeItems, report.ErrorReport);
        }

        var artifact = await artifacts.WriteItemsAsync(message.RunId, items, key, cancellationToken);

        if (modelKey is not null)
        {
            await journal.PinModelAsync(message.RunId, AgentRole.ItemTyper, modelKey, cancellationToken);
        }

        var nonStandalone = items.Count(i => !i.IsStandalone);

        logger.LogInformation(
            "Run {RunId}: {Items} items over {Paragraphs} displayable paragraphs; "
            + "{NonStandalone} not stageable alone ({Rate:P1}).",
            message.RunId, items.Count, displayable.Count, nonStandalone,
            items.Count == 0 ? 0 : (double)nonStandalone / items.Count);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.TypeItems, artifact.Key,
            // The IsStandalone rate is instrumented per run because it prices the re-writer and
            // gates nothing (§13, S3). It is a measurement, not a threshold.
            $"{items.Count} items, {items.Sum(i => i.Tags.Count)} candidate tags, "
            + $"{nonStandalone} non-standalone", cancellationToken);

        return new ItemsComplete(message.RunId, artifact, items.Count, message.Passed);
    }

    /// <summary>
    /// Runs of displayable paragraphs with overlap context. The commit region is what the window's
    /// answer is kept for; the overlap exists so a paragraph opening with a pronoun is not read in
    /// isolation.
    /// </summary>
    private IReadOnlyList<ItemWindow> PlanWindows(IReadOnlyList<Paragraph> displayable)
    {
        if (displayable.Count == 0)
        {
            return [];
        }

        var size = Math.Max(1, _items.ParagraphsPerWindow);
        var overlap = Math.Clamp(_items.WindowOverlapParagraphs, 0, size - 1);
        var windows = new List<ItemWindow>();

        for (var start = 0; start < displayable.Count; start += size)
        {
            var commitEnd = Math.Min(start + size, displayable.Count);
            var contextStart = Math.Max(0, start - overlap);
            var contextEnd = Math.Min(displayable.Count, commitEnd + overlap);

            windows.Add(new ItemWindow(
                windows.Count,
                [.. displayable.Skip(contextStart).Take(contextEnd - contextStart)],
                start - contextStart,
                commitEnd - contextStart));
        }

        return windows;
    }

    internal static IReadOnlyDictionary<string, string> SectionOfParagraph(SectionNode root) =>
        root.Descend()
            .SelectMany(n => n.ParagraphIds.Select(p => (Paragraph: p, n.SectionId)))
            .ToDictionary(x => x.Paragraph, x => x.SectionId, StringComparer.Ordinal);
}
