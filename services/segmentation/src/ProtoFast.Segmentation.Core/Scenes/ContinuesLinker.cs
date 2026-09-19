using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Scenes;

/// <summary>
/// How a <c>Continues</c> link arose, split by the provenance of the boundary that forced it
/// [unit §9].
///
/// <para>The split is the whole point of measuring: a <c>Continues</c> across a <b>trusted</b>
/// heading is expected and needs no action — chapters interrupt scenes constantly — while one
/// across an <b>inferred</b> boundary is evidence the inference was wrong, and above a per-family
/// threshold becomes a review finding routed back to structure repair. The section tree yields to
/// staging only where the tree was itself inferred.</para>
/// </summary>
public enum ContinuesProvenance
{
    TrustedHeading,
    InferredBoundary,
}

/// <summary>The links produced, and the measurement [unit §9.1] they carry.</summary>
public sealed record ContinuesResult(
    IReadOnlyList<Scene> Scenes,
    IReadOnlyList<SceneLink> Links,
    int AcrossTrustedHeadings,
    int AcrossInferredBoundaries);

/// <summary>
/// Phase 10's deterministic half of K4 (scene plan §8.9): <c>Continues</c> is <b>the C12 split</b>
/// — a cut forced at a section boundary across which the situation is unchanged.
///
/// <para>It needs no model because it needs no judgement: the two halves are adjacent, the
/// coordinates are already assigned, and "did the situation change" is equality on a tuple. The
/// five links that relate <em>distant</em> scenes are the ones that cannot be derived, and they are
/// phase 11's (§8.9).</para>
///
/// <para>C12 surfaces here as a link rather than as a validation warning, which is the point of
/// scenes being section children (§2): the disagreement between the structural boundary and the
/// staging boundary is <b>recorded</b> rather than hidden, and recording it is what makes it
/// measurable across a corpus.</para>
/// </summary>
public static class ContinuesLinker
{
    public static ContinuesResult Link(IReadOnlyList<Scene> scenes, SectionNode root)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(root);

        var inferredSections = root.Descend()
            .Where(n => n.HeadingLineId is null || n.TitleInferred)
            .Select(n => n.SectionId)
            .ToHashSet(StringComparer.Ordinal);

        var links = new List<SceneLink>();
        var trusted = 0;
        var inferred = 0;

        for (var i = 1; i < scenes.Count; i++)
        {
            var previous = scenes[i - 1];
            var current = scenes[i];

            if (string.Equals(previous.SectionId, current.SectionId, StringComparison.Ordinal))
            {
                // Inside one section a cut is a real situation change, by construction: the cutter
                // only cuts where a coordinate changed.
                continue;
            }

            if (!SameSituation(previous.Situation, current.Situation))
            {
                continue;
            }

            var provenance = inferredSections.Contains(current.SectionId)
                ? ContinuesProvenance.InferredBoundary
                : ContinuesProvenance.TrustedHeading;

            if (provenance == ContinuesProvenance.InferredBoundary)
            {
                inferred++;
            }
            else
            {
                trusted++;
            }

            links.Add(new SceneLink(
                current.SceneId,
                previous.SceneId,
                SceneLinkKind.Continues,
                [previous.SceneId, current.SceneId, current.SectionId],
                Confidence: 1.0));
        }

        var byScene = links
            .SelectMany(link => new[] { (link.FromSceneId, Link: link), (link.ToSceneId, Link: link) })
            .GroupBy(x => x.Item1, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Link).ToList(), StringComparer.Ordinal);

        return new ContinuesResult(
            [
                .. scenes.Select(s => byScene.TryGetValue(s.SceneId, out var own)
                    ? s with { Links = [.. s.Links, .. own] }
                    : s),
            ],
            links,
            trusted,
            inferred);
    }

    /// <summary>
    /// Equality on the five coordinates. Subject is compared case-insensitively and Time is
    /// compared on its <em>relation</em> rather than its anchor: a scene that continues across a
    /// chapter break is by definition <see cref="TimeRelation.Continuous"/> with the one before it,
    /// so requiring identical anchors would make the link fire almost never.
    /// </summary>
    internal static bool SameSituation(Situation a, Situation b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return a.Mode == b.Mode
            && string.Equals(a.PlaceId, b.PlaceId, StringComparison.Ordinal)
            && b.Time.Relation is TimeRelation.Continuous or TimeRelation.Unanchored
            && string.Equals(a.Subject.Text, b.Subject.Text, StringComparison.OrdinalIgnoreCase)
            && a.Cast.Select(c => c.PersonaId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(b.Cast.Select(c => c.PersonaId));
    }
}
