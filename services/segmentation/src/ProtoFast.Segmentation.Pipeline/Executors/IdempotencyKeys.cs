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
    /// Augmentation is keyed by the paragraph's <em>content</em> hash as well as its id, because a
    /// phase-6 repair can produce a new paragraph with a recycled id lineage. Without the hash, a
    /// re-run would reuse an augmentation written for different text (plan §12.2).
    /// </summary>
    public static string Augmentation(
        string runId, string augmentationType, string paragraphId, string paragraphHash, string promptVersion) =>
        $"{runId}:{augmentationType}:{paragraphId}:{paragraphHash}:{promptVersion}";
}
