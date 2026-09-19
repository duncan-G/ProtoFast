using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;

namespace ProtoFast.Segmentation.Core.Scenes;

/// <summary>
/// The digest list phase 11 reasons over, and the deterministic pass that decides whether phase 11
/// runs at all (scene plan §8.9).
///
/// <para><b>The input is digests, never text.</b> A scene digest is its id, title, mode, Time, cast
/// ids, place id, subject and the span of its Transition item — which is what a link decision
/// actually reads, and is orders of magnitude smaller than the scenes themselves. A thousand-scene
/// novel presents as a digest list, not as a document.</para>
/// </summary>
public static class SceneDigests
{
    public static IReadOnlyList<SceneDigest> Build(
        IReadOnlyList<Scene> scenes,
        IReadOnlyList<SceneItem> items,
        IReadOnlyDictionary<string, string> paragraphText)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(paragraphText);

        var byId = items.ToDictionary(i => i.ItemId, StringComparer.Ordinal);

        return
        [
            .. scenes.Select((scene, ordinal) => new SceneDigest(
                scene.SceneId,
                scene.SectionId,
                ordinal,
                scene.Title,
                scene.Situation.Mode,
                scene.Situation.Time,
                [.. scene.Situation.Cast.Select(c => c.PersonaId)],
                scene.Situation.PlaceId,
                scene.Situation.Subject.Text,
                TransitionText(scene, byId, paragraphText))),
        ];
    }

    /// <summary>
    /// The scene's opening Transition span, read out of the frozen paragraph. It is the one piece of
    /// document text a digest carries, and it carries it because a title card — "Three years later"
    /// — is the single strongest link signal there is.
    /// </summary>
    private static string? TransitionText(
        Scene scene,
        IReadOnlyDictionary<string, SceneItem> items,
        IReadOnlyDictionary<string, string> paragraphText)
    {
        var transition = scene.ItemIds
            .Select(items.GetValueOrDefault)
            .OfType<SceneItem>()
            .FirstOrDefault(i => i.Kind == ItemKind.Transition);

        return transition is not null && paragraphText.TryGetValue(transition.ParagraphId, out var text)
            ? transition.SpanOf(text).Trim()
            : null;
    }

    /// <summary>
    /// Windows of consecutive scenes with overlap. The overlap is what lets a windower settle a
    /// near frame by itself; the ones it cannot settle become <c>openQuestions</c>, which is
    /// exactly the case the orchestrator exists for and which no windowing removes.
    /// </summary>
    public static IReadOnlyList<SceneLinkWindow> Plan(
        IReadOnlyList<SceneDigest> digests, SceneLinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(digests);
        ArgumentNullException.ThrowIfNull(options);

        if (digests.Count == 0)
        {
            return [];
        }

        var size = Math.Max(2, options.ScenesPerWindow);
        var overlap = Math.Clamp(options.WindowOverlapScenes, 0, size - 1);
        var windows = new List<SceneLinkWindow>();

        for (var start = 0; start < digests.Count; start += size)
        {
            var commitEnd = Math.Min(start + size, digests.Count);
            var contextStart = Math.Max(0, start - overlap);

            windows.Add(new SceneLinkWindow(
                windows.Count,
                [.. digests.Skip(contextStart).Take(commitEnd - contextStart)],
                start,
                commitEnd));
        }

        return windows;
    }

    /// <summary>
    /// <b>The phase is skippable, and usually skipped.</b> A run whose scenes yield no candidate —
    /// no backward Time relation, no frame signal, no Transition pointing at an earlier situation —
    /// makes <b>zero model calls</b>, the same way a clean heading hierarchy skips the structure
    /// agents. Most textbooks and most transcripts have no frames and no flashbacks and pay nothing
    /// for the phase.
    /// </summary>
    public static IReadOnlyList<SceneDigest> Candidates(IReadOnlyList<SceneDigest> digests)
    {
        ArgumentNullException.ThrowIfNull(digests);

        var candidates = new List<SceneDigest>();

        for (var i = 0; i < digests.Count; i++)
        {
            var digest = digests[i];

            if (digest.Time.Relation is TimeRelation.Earlier or TimeRelation.Simultaneous)
            {
                candidates.Add(digest);
                continue;
            }

            if (digest.TransitionText is { Length: > 0 } transition && PointsBackwards(transition))
            {
                candidates.Add(digest);
                continue;
            }

            // A frame signal: a Narrated scene inside a run of Enacted ones, or the reverse — a
            // story being told inside a scene, which is [unit §7 case 4] and the reason FramedBy
            // exists at all.
            if (i > 0 && i + 1 < digests.Count
                && digest.Mode != digests[i - 1].Mode
                && digests[i - 1].Mode == digests[i + 1].Mode)
            {
                candidates.Add(digest);
            }
        }

        return candidates;
    }

    private static bool PointsBackwards(string transition) =>
        BackwardMarkers.Any(marker => transition.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Surface markers that a transition looks back rather than forward. Deliberately a short,
    /// explicit list: its only job is to decide whether phase 11 is worth a model call, and a
    /// false positive costs one skippable call while a false negative costs a missing link.
    /// </summary>
    private static readonly string[] BackwardMarkers =
    [
        "earlier", "before", "ago", "back in", "back then", "years before", "that same",
        "meanwhile", "at the same time", "elsewhere", "once,", "she remembered", "he remembered",
        "had been", "flashback",
    ];
}
