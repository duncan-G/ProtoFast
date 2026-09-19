using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Core.Scenes;

/// <summary>The scenes with their links, or the exact reason the plan could not be applied.</summary>
public sealed record SceneLinkMaterializationResult(
    IReadOnlyList<Scene> Scenes,
    IReadOnlyList<SceneLink> Links,
    ValidationResult Validation)
{
    public bool Success => Validation.Passed;
}

/// <summary>
/// Applies a <see cref="SceneLinkPlan"/> (scene plan §8.9). Pure code, and the only thing in phase
/// 11 that can produce a link.
///
/// <para>Four rules are <b>enforced</b> here so they cannot be violated rather than being caught
/// afterwards, which is why <c>link-integrity</c> and <c>link-evidence</c> are properties of this
/// class and appear in §9's table only because the freeze re-runs them over stored artifacts.</para>
///
/// <list type="number">
/// <item><b>Endpoints exist and differ.</b> No dangling id, no self-link.</item>
/// <item><b>Inverses are materialized in pairs</b>, so K5's "every scene that frames another" is an
/// index lookup from either end.</item>
/// <item><b>Orientation follows document order.</b> A plan proposing a forward <c>FlashbackOf</c>
/// is rejected with an exact error rather than silently flipped — the orchestrator disagreeing with
/// document order is a signal, not a typo to repair.</item>
/// <item><b>Evidence or nothing.</b> An uncited link is not stored.</item>
/// </list>
/// </summary>
public static class SceneLinkMaterializer
{
    public const string LinkPlanCheck = "link-plan";

    public static SceneLinkMaterializationResult Apply(
        SceneLinkPlan plan,
        IReadOnlyList<Scene> scenes,
        SceneLinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(options);

        var order = scenes
            .Select((s, index) => (s.SceneId, index))
            .ToDictionary(x => x.SceneId, x => x.index, StringComparer.Ordinal);

        var errors = new List<string>();
        var accepted = new List<SceneLink>();
        var seen = new HashSet<(string From, string To, SceneLinkKind Kind)>();

        // Links already on the scenes are phase 10's deterministic Continues (§8.9). They are kept
        // and counted against the fan cap, because a scene that continues another and is also
        // framed by a third has two links, not one of each budget.
        foreach (var existing in scenes.SelectMany(s => s.Links))
        {
            if (seen.Add((existing.FromSceneId, existing.ToSceneId, existing.Kind)))
            {
                accepted.Add(existing);
            }
        }

        for (var i = 0; i < plan.Links.Count; i++)
        {
            var proposal = plan.Links[i];
            var label = $"link {i + 1}";

            if (!Enum.TryParse<SceneLinkKind>(proposal.Kind, ignoreCase: true, out var kind))
            {
                errors.Add($"{label}: '{proposal.Kind}' is not one of the six link kinds");
                continue;
            }

            if (!order.TryGetValue(proposal.From, out var from))
            {
                errors.Add($"{label}: '{proposal.From}' is not a scene this run produced");
                continue;
            }

            if (!order.TryGetValue(proposal.To, out var to))
            {
                errors.Add($"{label}: '{proposal.To}' is not a scene this run produced");
                continue;
            }

            if (from == to)
            {
                errors.Add($"{label}: a scene cannot link to itself");
                continue;
            }

            var evidence = proposal.Evidence.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            if (evidence.Count == 0)
            {
                errors.Add(
                    $"{label}: cites nothing. Every link names the ids justifying it — a Transition "
                    + "item, a Time relation of Earlier or Simultaneous, or a place or cast identity "
                    + "with the earlier scene (C13).");
                continue;
            }

            // Orientation. ConcurrentWith is symmetric; every other kind that relates a scene to an
            // earlier one points backwards, and Frames points at what it contains.
            var orientationError = kind switch
            {
                SceneLinkKind.ConcurrentWith => null,
                SceneLinkKind.Frames when to <= from =>
                    $"{label}: Frames points forward to the scene it contains, but '{proposal.To}' "
                    + $"precedes '{proposal.From}' in the document",
                SceneLinkKind.FlashbackOf or SceneLinkKind.FramedBy or SceneLinkKind.ReturnsTo
                    or SceneLinkKind.Continues when to >= from =>
                    $"{label}: {kind} points backwards, but '{proposal.To}' does not precede "
                    + $"'{proposal.From}' in the document",
                _ => null,
            };

            if (orientationError is not null)
            {
                errors.Add(orientationError);
                continue;
            }

            if (!seen.Add((proposal.From, proposal.To, kind)))
            {
                continue;
            }

            accepted.Add(new SceneLink(proposal.From, proposal.To, kind, evidence, proposal.Confidence));
        }

        if (errors.Count > 0)
        {
            return new SceneLinkMaterializationResult(
                scenes, [], ValidationResult.Fail(LinkPlanCheck, errors));
        }

        var withInverses = AddInverses(accepted, seen);
        var capped = Cap(withInverses, options.MaxLinksPerScene);

        var byScene = capped
            .SelectMany(link => new[] { (Scene: link.FromSceneId, Link: link), (Scene: link.ToSceneId, Link: link) })
            .GroupBy(x => x.Scene, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Link).Distinct().ToList(), StringComparer.Ordinal);

        return new SceneLinkMaterializationResult(
            [.. scenes.Select(s => s with { Links = byScene.GetValueOrDefault(s.SceneId, []) })],
            capped,
            ValidationResult.Pass(LinkPlanCheck));
    }

    /// <summary>
    /// Rule 2: <c>Frames</c>/<c>FramedBy</c> and <c>ConcurrentWith</c> are stored on both scenes or
    /// on neither. Materializing the inverse here rather than asking the orchestrator for it is
    /// what makes the pairing an invariant instead of a thing that is usually true.
    /// </summary>
    private static List<SceneLink> AddInverses(
        List<SceneLink> links, HashSet<(string From, string To, SceneLinkKind Kind)> seen)
    {
        var result = new List<SceneLink>(links);

        foreach (var link in links)
        {
            var inverse = link.Kind switch
            {
                SceneLinkKind.Frames => SceneLinkKind.FramedBy,
                SceneLinkKind.FramedBy => SceneLinkKind.Frames,
                SceneLinkKind.ConcurrentWith => SceneLinkKind.ConcurrentWith,
                _ => (SceneLinkKind?)null,
            };

            if (inverse is { } kind && seen.Add((link.ToSceneId, link.FromSceneId, kind)))
            {
                result.Add(new SceneLink(
                    link.ToSceneId, link.FromSceneId, kind, link.EvidenceIds, link.Confidence));
            }
        }

        return result;
    }

    /// <summary>
    /// <c>MaxLinksPerScene</c>, applied by confidence. A scene accumulating a dozen links is one the
    /// orchestrator has speculated about; keeping the best four is the bound §8.9 asks for, and
    /// dropping the weakest is the only ordering that does not depend on the order they arrived in.
    /// </summary>
    private static List<SceneLink> Cap(List<SceneLink> links, int maxPerScene)
    {
        if (maxPerScene <= 0)
        {
            return links;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var kept = new List<SceneLink>(links.Count);

        foreach (var link in links.OrderByDescending(l => l.Confidence)
                     .ThenBy(l => l.FromSceneId, StringComparer.Ordinal))
        {
            var from = counts.GetValueOrDefault(link.FromSceneId);
            var to = counts.GetValueOrDefault(link.ToSceneId);

            if (from >= maxPerScene || to >= maxPerScene)
            {
                continue;
            }

            counts[link.FromSceneId] = from + 1;
            counts[link.ToSceneId] = to + 1;
            kept.Add(link);
        }

        return kept;
    }
}
