using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Data.Entities;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Pipeline.Augmentation;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Phases 10 and 11 (plan §12): augment every eligible paragraph, then review a sample.
///
/// <para>Input comes only from the frozen artifact. That is the whole point of the freeze: a
/// re-run augments exactly the same paragraphs, and the idempotency key — which includes the
/// paragraph's content hash — means a re-delivered message costs nothing at all.</para>
/// </summary>
public sealed class AugmentExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    PhaseGate gate,
    AugmenterAgent augmenter,
    IAugmentationCatalogue catalogue,
    PromptAssets assets,
    SceneContextFactory sceneContexts,
    IServiceScopeFactory scopes,
    IOptions<PipelineOptions> options,
    ILogger<AugmentExecutor> logger)
    : Executor<FrozenComplete, PublishComplete>(ExecutorIds.Augment, declareCrossRunShareable: true)
{
    private readonly AugmentationOptions _augmentation = options.Value.Augmentation;

    public override async ValueTask<PublishComplete> HandleAsync(
        FrozenComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var frozen = await artifacts.ReadFrozenAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Augment, "The frozen artifact is missing.");
        var run = await journal.LoadRunAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Augment, "The run row is missing.");

        var requested = run.Augmentations
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(catalogue.Find)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        var records = new List<AugmentationRecord>();

        if (requested.Count == 0)
        {
            await journal.SkipAsync(
                message.RunId, PipelinePhase.Augment, "no augmentation types requested", cancellationToken);
            await journal.SkipAsync(
                message.RunId, PipelinePhase.ReviewAugmentation, "nothing to review", cancellationToken);
        }
        else
        {
            await journal.StartAsync(
                message.RunId, PipelinePhase.Augment,
                IdempotencyKeys.Phase(message.RunId, PipelinePhase.Augment), cancellationToken);

            foreach (var type in requested)
            {
                // Granularity is a property of the TYPE, not of the pipeline (scene plan §10). The
                // fan-out, the idempotency key and the artifact layout are the same either way; what
                // differs is what one call covers, which is the type's own declaration.
                records.AddRange(type is IItemAugmentationType item
                    ? await AugmentAllItemsAsync(item, frozen, run, cancellationToken)
                    : await AugmentAllAsync(type, frozen, run, cancellationToken));
            }

            await journal.CompleteAsync(
                message.RunId, PipelinePhase.Augment, null,
                $"{records.Count} augmentations across {requested.Count} types", cancellationToken);

            records = await ReviewSampleAsync(requested, records, frozen, run, cancellationToken);
        }

        return await PublishAsync(message, frozen, run, records, cancellationToken);
    }

    /// <summary>
    /// Fans out across paragraphs in bounded batches, skipping any whose artifact already carries
    /// this idempotency key (plan §12.2).
    /// </summary>
    private async Task<List<AugmentationRecord>> AugmentAllAsync(
        IAugmentationType type,
        FrozenDocument frozen,
        SegmentationRun run,
        CancellationToken ct)
    {
        var promptVersion = assets.VersionFor(AgentRole.Augmenter);
        var structureContext = new StructureContext(
            run.RunId, run.DocumentId, frozen.DocumentTitle, run.DocumentFamily, run.Sensitivity,
            Instincts: [], PinnedStructurerKey: null, PinnedLevelerKey: null);

        var eligible = frozen.Paragraphs.Where(type.Applies).ToList();
        var skipped = frozen.Paragraphs.Count - eligible.Count;

        logger.LogInformation(
            "Run {RunId}: augmenting {Eligible} of {Total} paragraphs with '{Type}' ({Skipped} skipped)",
            run.RunId, eligible.Count, frozen.Paragraphs.Count, type.Name, skipped);

        var records = new List<AugmentationRecord>(eligible.Count);

        foreach (var batch in eligible.Chunk(_augmentation.FanOutBatchSize))
        {
            var tasks = batch.Select(async paragraph =>
            {
                var key = IdempotencyKeys.Augmentation(
                    run.RunId, type.Name, paragraph.ParagraphId, paragraph.ContentHash, promptVersion);

                var artifactKey = Storage.ArtifactKeys.Augmentation(run.RunId, type.Name, paragraph.ParagraphId);

                if (await gate.AlreadyDoneAsync(artifactKey, key, ct) is not null
                    && await artifacts.ReadAugmentationAsync(run.RunId, type.Name, paragraph.ParagraphId, ct) is { } cached)
                {
                    return cached;
                }

                var augmentationContext = type.BuildContext(
                    frozen.Root, paragraph, frozen.Paragraphs, frozen.DocumentTitle);

                var output = await augmenter.AugmentAsync(type, augmentationContext, paragraph, structureContext, ct);
                if (output is null)
                {
                    return null;
                }

                var record = new AugmentationRecord(
                    output.ParagraphId, output.Type, output.Json, output.ReviewVerdict);

                await artifacts.WriteAugmentationAsync(run.RunId, type.Name, record, key, ct);
                return record;
            });

            records.AddRange((await Task.WhenAll(tasks)).Where(r => r is not null).Select(r => r!));

            await journal.NoteAsync(
                run.RunId, PipelinePhase.Augment,
                $"{records.Count} of {eligible.Count} paragraphs augmented with '{type.Name}'", ct);
        }

        return records;
    }

    /// <summary>
    /// Fans out an item-scoped type over the frozen items (scene plan §3.6).
    ///
    /// <para>The selector is the type's own, and for the re-writer it is <c>IsStandalone == false</c>
    /// — so an item whose span already stands alone, which is the large majority, costs nothing.
    /// That is what makes the re-writer's cost scale with what is rendered rather than with the
    /// corpus: a document nobody stages pays for no rewrites at all.</para>
    ///
    /// <para>The idempotency key is the <em>item's</em> hash, so regenerating render text re-derives
    /// a key rather than invalidating an artifact — which is why running this after the freeze
    /// cannot disturb it.</para>
    /// </summary>
    private async Task<List<AugmentationRecord>> AugmentAllItemsAsync(
        IItemAugmentationType type,
        FrozenDocument frozen,
        SegmentationRun run,
        CancellationToken ct)
    {
        var promptVersion = assets.VersionFor(AgentRole.Augmenter);
        var eligible = frozen.Items.Where(type.Applies).ToList();

        if (eligible.Count == 0)
        {
            logger.LogInformation(
                "Run {RunId}: no item needs '{Type}' — every span stands alone.", run.RunId, type.Name);

            return [];
        }

        var paragraphText = frozen.Paragraphs.ToDictionary(p => p.ParagraphId, p => p.Text, StringComparer.Ordinal);
        var personas = frozen.Registries.Personas.ToDictionary(p => p.PersonaId, StringComparer.Ordinal);
        var sceneOfItem = frozen.Scenes
            .SelectMany(scene => scene.ItemIds.Select(id => (Item: id, scene.SceneId)))
            .ToDictionary(x => x.Item, x => x.SceneId, StringComparer.Ordinal);

        var sceneContext = await sceneContexts.CreateAsync(run.RunId, ct);
        var records = new List<AugmentationRecord>(eligible.Count);

        logger.LogInformation(
            "Run {RunId}: augmenting {Eligible} of {Total} items with '{Type}' ({Rate:P1} not standalone)",
            run.RunId, eligible.Count, frozen.Items.Count, type.Name,
            frozen.Items.Count == 0 ? 0 : (double)eligible.Count / frozen.Items.Count);

        foreach (var batch in eligible.Chunk(_augmentation.FanOutBatchSize))
        {
            var tasks = batch.Select(async item =>
            {
                if (!paragraphText.TryGetValue(item.ParagraphId, out var text))
                {
                    return null;
                }

                var key = IdempotencyKeys.ItemAugmentation(
                    run.RunId, type.Name, item.ItemId, ItemHash(item), promptVersion);

                var artifactKey = Storage.ArtifactKeys.Augmentation(run.RunId, type.Name, item.ItemId);

                if (await gate.AlreadyDoneAsync(artifactKey, key, ct) is not null
                    && await artifacts.ReadAugmentationAsync(run.RunId, type.Name, item.ItemId, ct) is { } cached)
                {
                    return cached;
                }

                // Exactly the personas the item's OWN tags bind (§3.7 row two) — not the scene's
                // cast, and not the registry. A name the item never referred to is an invention even
                // when the person is standing in the room.
                var resolved = item.Tags
                    .Where(t => t.Kind is TagKind.Persona or TagKind.Group && t.ReferentId is not null)
                    .Select(t => personas.GetValueOrDefault(t.ReferentId!))
                    .OfType<Persona>()
                    .DistinctBy(p => p.PersonaId)
                    .ToList();

                var context = type.BuildContext(
                    item, text, resolved, sceneOfItem.GetValueOrDefault(item.ItemId, string.Empty));

                var rendered = await augmenter.AugmentItemAsync(type, context, sceneContext, ct);
                if (rendered is null)
                {
                    return null;
                }

                var record = new AugmentationRecord(
                    item.ItemId, type.Name,
                    System.Text.Json.JsonSerializer.Serialize(new { renderText = rendered }),
                    "unreviewed");

                await artifacts.WriteAugmentationAsync(run.RunId, type.Name, record, key, ct);
                return record;
            });

            records.AddRange((await Task.WhenAll(tasks)).Where(r => r is not null).Select(r => r!));
        }

        await journal.NoteAsync(
            run.RunId, PipelinePhase.Augment,
            $"{records.Count} of {eligible.Count} items augmented with '{type.Name}'", ct);

        return records;
    }

    /// <summary>
    /// The item's identity for idempotency: its span and its kind, never its render text. Render
    /// text is generated metadata and is not part of what the item IS (§3.6), so regenerating it
    /// must not change the key it is stored under.
    /// </summary>
    private static string ItemHash(SceneItem item) =>
        Ids.Sha256Hex($"{item.ParagraphId}:{item.StartOffset}:{item.EndOffset}:{item.Kind}")[..16];

    /// <summary>
    /// Reviews a sample (plan §12.3). Restricted documents and unfamiliar families get every
    /// output reviewed; everything else gets the configured rate, chosen deterministically from
    /// the paragraph id so a re-run reviews the same sample rather than a fresh one.
    /// </summary>
    private async Task<List<AugmentationRecord>> ReviewSampleAsync(
        IReadOnlyList<IAugmentationType> types,
        List<AugmentationRecord> records,
        FrozenDocument frozen,
        SegmentationRun run,
        CancellationToken ct)
    {
        if (records.Count == 0)
        {
            return records;
        }

        await journal.StartAsync(
            run.RunId, PipelinePhase.ReviewAugmentation,
            IdempotencyKeys.Phase(run.RunId, PipelinePhase.ReviewAugmentation), ct);

        var rate = run.Sensitivity == Sensitivity.Restricted ? 1.0 : _augmentation.ReviewSampleRate;
        var byId = frozen.Paragraphs.ToDictionary(p => p.ParagraphId, StringComparer.Ordinal);
        var typesByName = types.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var structureContext = new StructureContext(
            run.RunId, run.DocumentId, frozen.DocumentTitle, run.DocumentFamily, run.Sensitivity,
            Instincts: [], PinnedStructurerKey: null, PinnedLevelerKey: null);

        var reviewed = 0;

        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];

            if (!InSample(record.ParagraphId, rate)
                || !byId.TryGetValue(record.ParagraphId, out var paragraph)
                || !typesByName.TryGetValue(record.Type, out var type))
            {
                continue;
            }

            var augmentationContext = type.BuildContext(
                frozen.Root, paragraph, frozen.Paragraphs, frozen.DocumentTitle);

            var output = await augmenter.ReviewAsync(
                type,
                new AugmentationOutput(record.ParagraphId, record.Type, record.Json, record.ReviewVerdict, null),
                paragraph, augmentationContext, structureContext, ct);

            records[i] = record with { Json = output.Json, ReviewVerdict = output.ReviewVerdict };
            reviewed++;
        }

        await journal.CompleteAsync(
            run.RunId, PipelinePhase.ReviewAugmentation, null,
            $"{reviewed} of {records.Count} augmentations reviewed", ct);

        return records;
    }

    /// <summary>
    /// Deterministic sampling: a hash of the paragraph id decides, so the same run re-sampled
    /// reviews the same outputs. A random sample would make a re-run's review results
    /// incomparable with the original's, which is the one thing a sample is for.
    /// </summary>
    internal static bool InSample(string paragraphId, double rate) =>
        rate >= 1.0 || (rate > 0 && Bucket(paragraphId) < rate);

    private static double Bucket(string value)
    {
        var hash = Ids.Sha256Hex(value);
        return Convert.ToInt32(hash[..6], 16) / (double)0xFFFFFF;
    }

    /// <summary>Phase 12 (plan §9.13): the result row and artifact that make a run green.</summary>
    private async Task<PublishComplete> PublishAsync(
        FrozenComplete message,
        FrozenDocument frozen,
        SegmentationRun run,
        IReadOnlyList<AugmentationRecord> records,
        CancellationToken ct)
    {
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.Publish);
        await journal.StartAsync(message.RunId, PipelinePhase.Publish, key, ct);

        var review = await artifacts.ReadReviewAsync(message.RunId, ct);
        var publishedAt = DateTimeOffset.UtcNow;

        var result = new ResultArtifact(
            run.RunId, run.DocumentId, frozen.Root, frozen.Paragraphs, records,
            review?.Findings ?? [], frozen.TreeHash, frozen.FrozenAt, publishedAt)
        {
            Scenes = frozen.Scenes,
            Items = frozen.Items,
            Registries = frozen.Registries,
        };

        var artifact = await artifacts.WriteResultAsync(message.RunId, result, key, ct);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        // Upsert: a re-published run replaces its result rather than failing on the primary key.
        var existing = await db.RunResults.FirstOrDefaultAsync(r => r.RunId == run.RunId, ct);
        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

        // The scene columns write their enums as text, for the reason the Run entity gives for
        // doing the same: an integer that shifts when an enum member is inserted is a migration
        // hazard, and the scene enumerations are the ones this design expects to grow. The three
        // older columns keep their numeric spelling — rows already published are written that way,
        // and re-spelling them is a migration rather than a side effect of this change.
        var sceneJson = new System.Text.Json.JsonSerializerOptions(json)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        // The scene layers are published from the frozen document rather than re-read from their
        // own phase artifacts: the freeze is what bound them to this tree hash, so publishing
        // anything else would put a scene stream and a tree in one row that were never frozen
        // together (C10).
        var treeJson = System.Text.Json.JsonSerializer.Serialize(frozen.Root, json);
        var paragraphsJson = System.Text.Json.JsonSerializer.Serialize(frozen.Paragraphs, json);
        var augmentationsJson = System.Text.Json.JsonSerializer.Serialize(records, json);
        var scenesJson = System.Text.Json.JsonSerializer.Serialize(frozen.Scenes, sceneJson);
        var itemsJson = System.Text.Json.JsonSerializer.Serialize(frozen.Items, sceneJson);
        var registriesJson = System.Text.Json.JsonSerializer.Serialize(frozen.Registries, sceneJson);

        if (existing is null)
        {
            db.RunResults.Add(new RunResult
            {
                RunId = run.RunId,
                OwnerSubject = run.OwnerSubject,
                DocumentId = run.DocumentId,
                TreeJson = treeJson,
                ParagraphsJson = paragraphsJson,
                AugmentationsJson = augmentationsJson,
                ScenesJson = scenesJson,
                ItemsJson = itemsJson,
                RegistriesJson = registriesJson,
                TreeHash = frozen.TreeHash,
                FrozenAt = frozen.FrozenAt,
                PublishedAt = publishedAt,
            });
        }
        else
        {
            existing.TreeJson = treeJson;
            existing.ParagraphsJson = paragraphsJson;
            existing.AugmentationsJson = augmentationsJson;
            existing.ScenesJson = scenesJson;
            existing.ItemsJson = itemsJson;
            existing.RegistriesJson = registriesJson;
            existing.TreeHash = frozen.TreeHash;
            existing.FrozenAt = frozen.FrozenAt;
            existing.PublishedAt = publishedAt;
        }

        var runRow = await db.Runs.FirstOrDefaultAsync(r => r.RunId == run.RunId, ct);
        if (runRow is not null)
        {
            runRow.PublishedAt = publishedAt;
            runRow.UpdatedAt = publishedAt;
        }

        await db.SaveChangesAsync(ct);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.Publish, artifact.Key,
            $"published: {frozen.Paragraphs.Count} paragraphs, {frozen.Scenes.Count} scenes, "
            + $"{frozen.Items.Count} items, {records.Count} augmentations", ct);

        logger.LogInformation(
            "Run {RunId}: published {Paragraphs} paragraphs and {Augmentations} augmentations",
            message.RunId, frozen.Paragraphs.Count, records.Count);

        return new PublishComplete(message.RunId, frozen.TreeHash, frozen.Paragraphs.Count);
    }
}
