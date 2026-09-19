using System.Text.RegularExpressions;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Classification;

/// <summary>
/// The v1 composition families (scene plan §12). Chosen to span the two axes that actually vary —
/// dialogue density and setting explicitness — rather than to sample genres: the screenplay is
/// explicit on both, the textbook near-null on both, the novel dialogue-heavy with implicit
/// settings, the transcript dialogue-heavy with absent settings.
/// </summary>
public static class CompositionFamily
{
    public const string Unknown = "unknown";
    public const string Novel = "novel";
    public const string Textbook = "textbook";
    public const string Transcript = "transcript";
    public const string Screenplay = "screenplay";

    public static readonly IReadOnlyList<string> All = [Novel, Textbook, Transcript, Screenplay];
}

/// <summary>
/// Phase 5 step 5 (scene plan §8.5): the per-paragraph composition-family feature vector, and the
/// run family re-confirmed from it.
///
/// <para>The vector is emitted as a by-product rather than as a phase of its own because phase 7
/// needs it and cannot produce it — a scope root is a claim about a <em>section</em>, so it cannot
/// be made before the tree exists at phase 6, while the evidence for it can be and is (§6.1). That
/// split is what keeps family scopes free of a second detector and free of a second model call.
/// </para>
/// </summary>
public static partial class FamilyEvidenceExtractor
{
    public static IReadOnlyList<FamilyEvidence> Extract(IReadOnlyList<Paragraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        return [.. paragraphs.Select(Extract)];
    }

    public static FamilyEvidence Extract(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var text = paragraph.Text;
        var sentences = SentenceSplitter.Split(text);
        var words = text.Split(
            [' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new FamilyEvidence(
            paragraph.ParagraphId,
            DialogueRatio(text),
            SpeakerLabelPattern().IsMatch(text),
            sentences.Count == 0 ? 0 : (double)sentences.Count(Imperative) / sentences.Count,
            words.Length == 0 ? 0 : (double)words.Count(SecondPerson) / words.Length,
            words.Length == 0 ? 0 : (double)words.Count(PastTense) / words.Length,
            paragraph.WordCount);
    }

    /// <summary>
    /// Re-confirms the run's composition family from the aggregated vector (§6). Phase 0's guess
    /// was made from converter metadata and line shapes; this one is made from paragraphs, which is
    /// the first point at which dialogue ratio and tense mean anything.
    /// </summary>
    public static string Confirm(IReadOnlyList<FamilyEvidence> evidence, string fallback)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var profile = Aggregate(evidence);
        if (profile is null)
        {
            return fallback;
        }

        // Ordered by how decisive the signal is, not by how common the family is. A speaker label
        // on most paragraphs is close to proof; a past-tense dialogue mix is merely likely.
        if (profile.SpeakerLabelRate >= 0.5)
        {
            return profile.DialogueRatio >= 0.5 ? CompositionFamily.Screenplay : CompositionFamily.Transcript;
        }

        if (profile.DialogueRatio >= 0.15 && profile.PastTense >= 0.04)
        {
            return CompositionFamily.Novel;
        }

        if (profile.SecondPerson >= 0.01 || profile.ImperativeDensity >= 0.15)
        {
            return CompositionFamily.Textbook;
        }

        return fallback == FamilyDetector.Unknown && profile.DialogueRatio < 0.05
            ? CompositionFamily.Textbook
            : fallback;
    }

    /// <summary>
    /// The mean of a run of vectors, weighted by word count so a one-line paragraph does not carry
    /// the same vote as a page. Null for an empty or wordless run, which is not a profile.
    /// </summary>
    public static FamilyProfile? Aggregate(IEnumerable<FamilyEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var rows = evidence as IReadOnlyList<FamilyEvidence> ?? [.. evidence];
        var total = rows.Sum(e => (double)e.WordCount);

        if (rows.Count == 0 || total <= 0)
        {
            return null;
        }

        return new FamilyProfile(
            rows.Sum(e => e.DialogueRatio * e.WordCount) / total,
            rows.Sum(e => (e.HasSpeakerLabel ? 1d : 0d) * e.WordCount) / total,
            rows.Sum(e => e.ImperativeDensity * e.WordCount) / total,
            rows.Sum(e => e.SecondPerson * e.WordCount) / total,
            rows.Sum(e => e.PastTense * e.WordCount) / total,
            rows.Count);
    }

    /// <summary>Share of characters inside quotation marks, straight and curly alike.</summary>
    internal static double DialogueRatio(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var inside = 0;
        var open = false;

        foreach (var c in text)
        {
            if (c is '"' or '“' or '”' or '«' or '»')
            {
                open = !open;
                continue;
            }

            if (open)
            {
                inside++;
            }
        }

        return (double)inside / text.Length;
    }

    private static bool Imperative(string sentence)
    {
        var first = sentence.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is not null && ImperativeOpeners.Contains(first.Trim('.', ',', ':', ';'));
    }

    private static bool SecondPerson(string word) =>
        SecondPersonWords.Contains(word.Trim('.', ',', ':', ';', '"', '\'', '(', ')'));

    /// <summary>
    /// A regular-verb proxy, not a tagger. It is deliberately crude: the value is used to compare
    /// one part of a document against another, so a constant bias cancels — and a real tagger would
    /// be a dependency the phase does not otherwise need.
    /// </summary>
    private static bool PastTense(string word)
    {
        var trimmed = word.Trim('.', ',', ':', ';', '"', '\'', '(', ')').ToLowerInvariant();
        return trimmed.Length > 4 && trimmed.EndsWith("ed", StringComparison.Ordinal)
            || IrregularPast.Contains(trimmed);
    }

    private static readonly HashSet<string> ImperativeOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        "consider", "note", "observe", "notice", "recall", "suppose", "let", "assume", "compute",
        "calculate", "show", "prove", "find", "solve", "use", "apply", "define", "write", "draw",
        "explain", "describe", "compare", "list", "state", "verify", "check", "see", "read",
    };

    private static readonly HashSet<string> SecondPersonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "you", "your", "yours", "yourself", "yourselves",
    };

    private static readonly HashSet<string> IrregularPast = new(StringComparer.Ordinal)
    {
        "was", "were", "had", "said", "went", "came", "saw", "took", "got", "made", "knew",
        "thought", "found", "told", "felt", "left", "put", "stood", "sat", "ran", "held",
    };

    [GeneratedRegex(@"^\s*[\p{Lu}][\p{L}\p{N}.\-' ]{0,38}:\s", RegexOptions.Multiline)]
    private static partial Regex SpeakerLabelPattern();
}

/// <summary>The aggregated feature vector over a run of paragraphs — what a family is compared on.</summary>
public sealed record FamilyProfile(
    double DialogueRatio,
    double SpeakerLabelRate,
    double ImperativeDensity,
    double SecondPerson,
    double PastTense,
    int ParagraphCount)
{
    /// <summary>
    /// How far two profiles sit apart, on the five features the composition family is detected
    /// from. A weighted sum of absolute differences rather than a Euclidean distance, so the number
    /// stays readable as "how much these two disagree" — which is what the configured band is
    /// expressed in.
    ///
    /// <para>Two features carry a multiplier because their natural range is an order of magnitude
    /// smaller than the others': second-person density is a share of <em>words</em> and is a strong
    /// signal at 0.02, while dialogue ratio is a share of <em>characters</em> and is weak at 0.02.
    /// Without the weights the two features that most separate a textbook from a novel would be the
    /// two that never move the number.</para>
    /// </summary>
    public double DistanceTo(FamilyProfile other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Math.Abs(DialogueRatio - other.DialogueRatio)
            + Math.Abs(SpeakerLabelRate - other.SpeakerLabelRate)
            + Math.Abs(ImperativeDensity - other.ImperativeDensity)
            + Math.Abs(SecondPerson - other.SecondPerson) * 10
            + Math.Abs(PastTense - other.PastTense) * 5;
    }
}
