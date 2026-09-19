using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// The keys that make a re-run free (plan N4).
///
/// <para>Each one names everything that would change the output: the run, the phase, the unit of
/// work, and the prompt version. Leaving the prompt version out would make a phase "already done"
/// after a prompt change, which is precisely when it most needs re-running.</para>
/// </summary>
public static class IdempotencyKeys
{
    public static string Phase(string runId, PipelinePhase phase, string promptVersion = "det") =>
        $"{runId}:{phase.ToString().ToLowerInvariant()}:{promptVersion}";

    public static string Window(string runId, int windowIndex, string promptVersion) =>
        $"{runId}:label:{windowIndex}:{promptVersion}";

    /// <summary>
    /// One structuring window (orchestrator plan §6). Distinct from <see cref="Window"/> because
    /// a label window and a structure window with the same index are different units of work over
    /// different inputs, and one key for both would let a resumed run serve the wrong artifact.
    /// </summary>
    public static string StructureWindow(string runId, int windowIndex, string promptVersion) =>
        $"{runId}:structure-window:{windowIndex}:{promptVersion}";

    /// <summary>One phase-8 typing window (scene plan §8.6).</summary>
    public static string ItemWindow(string runId, int windowIndex, string promptVersion) =>
        $"{runId}:item-window:{windowIndex}:{promptVersion}";

    /// <summary>One phase-9 persona window (scene plan §8.7).</summary>
    public static string PersonaWindow(string runId, int windowIndex, string promptVersion) =>
        $"{runId}:persona-window:{windowIndex}:{promptVersion}";

    /// <summary>One phase-11 scene-link window (scene plan §8.9).</summary>
    public static string SceneLinkWindow(string runId, int windowIndex, string promptVersion) =>
        $"{runId}:scene-link-window:{windowIndex}:{promptVersion}";

    /// <summary>
    /// An item-scoped augmentation (scene plan §3.6). Keyed by the <em>item's</em> hash rather than
    /// a paragraph's, which is what makes re-running the re-writer free: render text is produced
    /// against a frozen item and keyed by it, so regenerating re-derives a key rather than
    /// invalidating an artifact.
    /// </summary>
    public static string ItemAugmentation(
        string runId, string augmentationType, string itemId, string itemHash, string promptVersion) =>
        $"{runId}:{augmentationType}:{itemId}:{itemHash}:{promptVersion}";

    /// <summary>
    /// Augmentation is keyed by the paragraph's <em>content</em> hash as well as its id, because a
    /// phase-6 repair can produce a new paragraph with a recycled id lineage. Without the hash, a
    /// re-run would reuse an augmentation written for different text (plan §12.2).
    /// </summary>
    public static string Augmentation(
        string runId, string augmentationType, string paragraphId, string paragraphHash, string promptVersion) =>
        $"{runId}:{augmentationType}:{paragraphId}:{paragraphHash}:{promptVersion}";
}
