using Microsoft.Extensions.Logging;
using ProtoFast.Segmentation.Core.Classification;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Pipeline.Executors;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>
/// Builds the <see cref="SceneContext"/> the five scene phases share, from the run row and the
/// instinct store.
///
/// <para>It exists because five executors need the same six facts and would otherwise each assemble
/// them slightly differently — and one of those facts, the scoped instinct set, is the difference
/// between an agent seeing guidance meant for it and an agent seeing guidance meant for another
/// (§7.1).</para>
/// </summary>
public sealed class SceneContextFactory(
    RunJournal journal,
    FamilyInstincts instincts,
    ILogger<SceneContextFactory> logger)
{
    public async Task<SceneContext> CreateAsync(string runId, CancellationToken ct = default)
    {
        var run = await journal.LoadRunAsync(runId, ct)
            ?? throw new PipelineFailureException(
                PipelinePhase.ClassifyPresentation, $"The run row for '{runId}' is missing.");

        // Both axes are drawn separately, because a scanned PDF of a novel and a scanned PDF of a
        // textbook share every production instinct and almost no composition instinct (§6).
        var byScope = new Dictionary<InstinctScope, IReadOnlyList<string>>();

        foreach (var scope in Enum.GetValues<InstinctScope>())
        {
            var axis = scope switch
            {
                InstinctScope.Labeling or InstinctScope.Structure => FamilyAxis.Production,
                _ => FamilyAxis.Composition,
            };

            var family = axis == FamilyAxis.Production ? run.DocumentFamily : run.CompositionFamily;

            byScope[scope] = await instincts.ForScopeAsync(family, axis, scope, ct);

            // Metadata draws from both axes: a publisher's template decides where the front matter
            // ends, and the kind of work decides whether an epigraph is part of it (§7.2).
            if (scope == InstinctScope.Metadata && run.DocumentFamily != run.CompositionFamily)
            {
                byScope[scope] =
                [
                    .. byScope[scope],
                    .. await instincts.ForScopeAsync(
                        run.DocumentFamily, FamilyAxis.Production, scope, ct),
                ];
            }
        }

        logger.LogDebug(
            "Run {RunId}: production family '{Production}', composition family '{Composition}'.",
            runId, run.DocumentFamily, run.CompositionFamily);

        return new SceneContext(
            run.RunId,
            run.DocumentId,
            run.DocumentId,
            run.DocumentFamily,
            string.IsNullOrWhiteSpace(run.CompositionFamily) ? CompositionFamily.Unknown : run.CompositionFamily,
            run.Sensitivity,
            byScope)
        {
            PinnedModels = run.PinnedModels
                .Where(kv => Enum.TryParse<AgentRole>(kv.Key, out _))
                .ToDictionary(kv => Enum.Parse<AgentRole>(kv.Key), kv => kv.Value),
        };
    }
}
