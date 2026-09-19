using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;

namespace ProtoFast.Segmentation.Core.Scenes;

/// <summary>Why code proposed a boundary. The reason decides whether the floor may overrule it (§8.8).</summary>
public enum BoundaryReason
{
    /// <summary>An explicit <see cref="ItemKind.Transition"/> item. Evidence — the floor never overrules it.</summary>
    Transition,

    /// <summary>The item mix implies a different mode. A mode change is always a cut [unit §4.2].</summary>
    ModeChange,

    /// <summary>Someone joined or left. Cast is the set present, not the current speaker [unit §3.3].</summary>
    CastChange,

    /// <summary>The section ends. Structural, so a scene cannot straddle it (C12).</summary>
    SectionBoundary,
}

/// <summary>A proposed cut before <see cref="ItemIndex"/>, with what justifies it (C13).</summary>
public sealed record BoundaryProposal(
    int ItemIndex,
    BoundaryReason Reason,
    SceneMode ModeAfter,
    IReadOnlyList<string> EvidenceIds)
{
    /// <summary>
    /// A marked boundary <em>is</em> evidence, and the floor never overrules evidence (§8.8 rule 2).
    /// That is what confines the length heuristic to prose that shifts without saying so.
    /// </summary>
    public bool IsMarked => Reason is BoundaryReason.Transition or BoundaryReason.SectionBoundary;
}

/// <summary>What the deterministic pass derived, and what the floor suppressed doing it.</summary>
public sealed record CutProposal(
    IReadOnlyList<BoundaryProposal> Boundaries,
    IReadOnlyList<SceneMode> ModeByItem,

    /// <summary>
    /// <c>scene-cuts-suppressed-by-floor</c> — the entire tuning signal for <c>MinSceneSpan</c>
    /// (§8.8 rule 3). Near zero and the floor is inert; large and it is setting the document's
    /// scene count by itself, which is the tripwire §13 fires on at 0.25 of candidate cuts.
    /// </summary>
    int SuppressedByFloor);

/// <summary>
/// The deterministic halves of phase 10 — steps 1 and 3 of scene plan §8.8, with the agent's
/// Setting and Subject in between.
///
/// <para>Typing items first is what makes this mostly deterministic: <b>mode is largely a function
/// of the item mix</b>, and mode selects which coordinates are cut-bearing at all [unit §4.2]. The
/// table's discriminating power sits in the Speech attributes rather than in the
/// Action/Description split, deliberately — mode is always a cut, so it must not hang on a
/// distinction the model gets wrong sometimes (§3.1, §8.3).</para>
///
/// <para>The phase is a fan-out rather than an orchestration for a reason the unit definition
/// supplies: setting inheritance is bounded to within a leaf section [unit §3.1.1], so it never
/// crosses a window and the join is guaranteed. A constraint written to cap error propagation buys
/// reproducibility too.</para>
/// </summary>
public static class SceneCutter
{
    /// <summary>
    /// Derives mode per item, cast per run, and the boundaries those imply, then applies the
    /// <c>MinSceneSpan</c> floor to the unmarked ones.
    /// </summary>
    public static CutProposal Propose(
        IReadOnlyList<SceneItem> items,
        IReadOnlyDictionary<string, string> paragraphText,
        int minSceneSpan)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(paragraphText);

        if (items.Count == 0)
        {
            return new CutProposal([], [], 0);
        }

        var modes = DeriveModes(items);
        var candidates = new List<BoundaryProposal>();

        for (var i = 1; i < items.Count; i++)
        {
            if (items[i].Kind == ItemKind.Transition)
            {
                candidates.Add(new BoundaryProposal(
                    i, BoundaryReason.Transition, modes[i], [items[i].ItemId]));
                continue;
            }

            if (modes[i] != modes[i - 1])
            {
                candidates.Add(new BoundaryProposal(
                    i, BoundaryReason.ModeChange, modes[i], [items[i - 1].ItemId, items[i].ItemId]));
                continue;
            }

            // Cast changes only cut where cast is live [unit §4.2] — in Enacted, and in
            // Expounded/Addressed where a new voice takes over. Under Narrated the cast is normally
            // constant and a change in who is mentioned is not a change in who is there.
            if (modes[i] is SceneMode.Enacted or SceneMode.Expounded or SceneMode.Addressed
                && items[i].Speech is { SpeakerPersonaId: { } speaker }
                && !SpeakersBefore(items, i).Contains(speaker))
            {
                candidates.Add(new BoundaryProposal(
                    i, BoundaryReason.CastChange, modes[i], [items[i].ItemId]));
            }
        }

        var kept = ApplyFloor(items, paragraphText, candidates, minSceneSpan, out var suppressed);

        return new CutProposal(kept, modes, suppressed);
    }

    /// <summary>
    /// The mode implied by the item mix over a small neighbourhood (§8.3). Read per item and then
    /// smoothed by the floor rather than per span, because the span is what the boundaries are
    /// being derived to produce.
    /// </summary>
    internal static IReadOnlyList<SceneMode> DeriveModes(IReadOnlyList<SceneItem> items)
    {
        var modes = new SceneMode[items.Count];
        var lastSpeech = (SpeechAttributes?)null;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            modes[i] = item.Kind switch
            {
                ItemKind.Exhibit => SceneMode.Exhibited,

                // The discriminating rows. Embodiment and Addressee separate Enacted from Narrated
                // and from Expounded, and they are assigned far more reliably than the
                // Action/Description split — which is exactly why mode keys on them.
                ItemKind.Speech => item.Speech switch
                {
                    { Embodiment: Embodiment.Embodied, Addressee: Addressee.InScene } => SceneMode.Enacted,
                    { Embodiment: Embodiment.Embodied, Addressee: Addressee.Audience } => SceneMode.Expounded,
                    { Embodiment: Embodiment.Embodied, Addressee: Addressee.Self } => SceneMode.Enacted,
                    { Embodiment: Embodiment.Disembodied, Addressee: Addressee.Audience } => SceneMode.Narrated,
                    _ => SceneMode.Narrated,
                },

                // Action and Description break ties; neither ever carries a mode decision alone.
                // They read the last utterance's attributes, so an action beside in-scene dialogue
                // is Enacted and the same action beside narration is Narrated.
                ItemKind.Action or ItemKind.Description => lastSpeech switch
                {
                    { Embodiment: Embodiment.Embodied, Addressee: Addressee.InScene } => SceneMode.Enacted,
                    { Embodiment: Embodiment.Embodied, Addressee: Addressee.Audience } => SceneMode.Expounded,
                    { Embodiment: Embodiment.Disembodied } => SceneMode.Narrated,
                    _ => item.Kind == ItemKind.Action ? SceneMode.Enacted : SceneMode.Narrated,
                },

                _ => SceneMode.Exhibited,
            };

            if (item.Speech is { } speech)
            {
                lastSpeech = speech;
            }
        }

        // A Transition is the title card of the scene it opens, so it takes that scene's mode
        // rather than one of its own. Giving it a mode would make it change the mode twice — into
        // the transition and out of it — and a mode change is always a cut, so a single marked
        // transition would produce two boundaries where the text marked one.
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Kind != ItemKind.Transition)
            {
                continue;
            }

            var following = Enumerable.Range(i + 1, items.Count - i - 1)
                .Where(j => items[j].Kind != ItemKind.Transition)
                .Select(j => (SceneMode?)modes[j])
                .FirstOrDefault();

            modes[i] = following ?? (i > 0 ? modes[i - 1] : SceneMode.Narrated);
        }

        return modes;
    }

    /// <summary>
    /// <b>An unmarked mode change spanning fewer than <c>MinSceneSpan</c> sentences is recorded as
    /// an item attribute, not a cut</b> (§3.5, C3).
    ///
    /// <para>This is the only thing standing between sub-paragraph cutting and one scene per
    /// clause, and it is deliberately narrow: an explicit <see cref="ItemKind.Transition"/> or a
    /// structural boundary <em>is</em> evidence, so a marked one-sentence cut stands. The floor
    /// exists for prose that shifts without saying so, which is the only place a length heuristic
    /// is doing any judging.</para>
    /// </summary>
    internal static IReadOnlyList<BoundaryProposal> ApplyFloor(
        IReadOnlyList<SceneItem> items,
        IReadOnlyDictionary<string, string> paragraphText,
        IReadOnlyList<BoundaryProposal> candidates,
        int minSceneSpan,
        out int suppressed)
    {
        suppressed = 0;

        if (minSceneSpan <= 1 || candidates.Count == 0)
        {
            return candidates;
        }

        var kept = new List<BoundaryProposal>(candidates.Count);

        for (var i = 0; i < candidates.Count; i++)
        {
            var boundary = candidates[i];

            if (boundary.IsMarked)
            {
                kept.Add(boundary);
                continue;
            }

            var runEnd = i + 1 < candidates.Count ? candidates[i + 1].ItemIndex : items.Count;

            if (SentenceSpan(items, paragraphText, boundary.ItemIndex, runEnd) >= minSceneSpan)
            {
                kept.Add(boundary);
                continue;
            }

            suppressed++;
        }

        return kept;
    }

    /// <summary>
    /// How many sentences a run of items covers. Scene boundaries are sentence-granular and never
    /// finer [unit §4.1] — a situation does not change halfway through a sentence, and a cut there
    /// would strand a clause — so this is the unit the floor is expressed in.
    /// </summary>
    internal static int SentenceSpan(
        IReadOnlyList<SceneItem> items,
        IReadOnlyDictionary<string, string> paragraphText,
        int start,
        int endExclusive)
    {
        var counted = 0;

        foreach (var group in items.Take(endExclusive).Skip(start).GroupBy(i => i.ParagraphId))
        {
            if (!paragraphText.TryGetValue(group.Key, out var text))
            {
                counted += group.Count();
                continue;
            }

            var spans = SentenceSplitter.Spans(text);
            var from = group.Min(i => i.StartOffset);
            var to = group.Max(i => i.EndOffset);

            counted += Math.Max(1, spans.Count(s => s.Start < to && s.End > from));
        }

        return counted;
    }

    /// <summary>
    /// Who has spoken in the current run. Cast is the set of people present, not the current
    /// speaker [unit §3.3] — without that rule dialogue shreds into one scene per turn, which is
    /// the single most expensive mistake available to a scene cutter.
    /// </summary>
    private static IReadOnlySet<string> SpeakersBefore(IReadOnlyList<SceneItem> items, int index)
    {
        var speakers = new HashSet<string>(StringComparer.Ordinal);

        for (var i = index - 1; i >= 0 && i >= index - CastMemory; i--)
        {
            if (items[i].Speech is { SpeakerPersonaId: { } id })
            {
                speakers.Add(id);
            }
        }

        return speakers;
    }

    /// <summary>
    /// Items looked back over to decide whether a speaker is new. Bounded rather than whole-scene
    /// because the scene does not exist yet — this pass is what produces it.
    /// </summary>
    private const int CastMemory = 40;
}
