using System.Text.RegularExpressions;

namespace ProtoFast.Segmentation.Core.Grounding;

/// <summary>
/// The closed-class allowlist of scene plan §3.7 — determiners, copulas, auxiliaries,
/// prepositions, conjunctions, pronouns, possessive markers and sentence punctuation.
///
/// <para><b>This list is what lets the grounding gate be hard.</b> Every false failure the naive
/// rule was feared to produce — a rewrite needing a copula the fragment lacks, or a possessive to
/// attach a name — is closed-class, and closed classes are finite, enumerable per language and
/// carry no referents. Admitting them wholesale removes the false failures without admitting one
/// content word: invention requires an open-class item, and those stay restricted to the span's own
/// lemmas and its own tags' personas.</para>
///
/// <para>The list is an <b>asset, not code</b> (§3.7): it ships as
/// <c>rules/closed-class.{lang}.txt</c> beside the prompts and is versioned with them, and a new
/// language is a file. This class is the loader and the English fallback — the fallback exists so
/// that a run whose language has no file still gets a hard gate rather than no gate.</para>
/// </summary>
public sealed partial class ClosedClassLexicon
{
    private readonly HashSet<string> _words;

    private ClosedClassLexicon(IEnumerable<string> words) =>
        _words = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses one <c>closed-class.{lang}.txt</c>: one lemma per line, <c>#</c> starts a comment.
    /// The format is deliberately trivial, because the audience for it is whoever adds a language
    /// rather than whoever maintains the pipeline.
    /// </summary>
    public static ClosedClassLexicon Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new ClosedClassLexicon(
            content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split('#', 2)[0].Trim())
                .Where(line => line.Length > 0));
    }

    /// <summary>English, used when the run's language has no asset of its own.</summary>
    public static readonly ClosedClassLexicon English = new(
    [
        // Determiners and quantifiers
        "a", "an", "the", "this", "that", "these", "those", "some", "any", "no", "every", "each",
        "both", "all", "another", "other", "such", "what", "which", "whose", "many", "much", "few",
        // Pronouns
        "i", "me", "my", "mine", "myself", "you", "your", "yours", "yourself", "yourselves",
        "he", "him", "his", "himself", "she", "her", "hers", "herself", "it", "its", "itself",
        "we", "us", "our", "ours", "ourselves", "they", "them", "their", "theirs", "themselves",
        "who", "whom", "someone", "somebody", "something", "anyone", "anybody", "anything",
        "everyone", "everybody", "everything", "one", "ones",
        // Copulas and auxiliaries
        "be", "am", "is", "are", "was", "were", "been", "being",
        "have", "has", "had", "having", "do", "does", "did", "doing",
        "will", "would", "shall", "should", "can", "could", "may", "might", "must", "ought",
        "not", "n't",
        // Prepositions
        "about", "above", "across", "after", "against", "along", "among", "around", "as", "at",
        "before", "behind", "below", "beneath", "beside", "between", "beyond", "by", "down",
        "during", "except", "for", "from", "in", "inside", "into", "like", "near", "of", "off",
        "on", "onto", "out", "outside", "over", "past", "since", "through", "throughout", "to",
        "toward", "towards", "under", "until", "up", "upon", "with", "within", "without",
        // Conjunctions and connectives
        "and", "or", "but", "nor", "so", "yet", "if", "then", "than", "because", "although",
        "though", "while", "whereas", "unless", "whether", "when", "where", "why", "how",
        // Possessive markers and particles
        "'s", "s'", "'", "there", "here", "too", "also", "just", "only", "still", "again",
    ]);

    /// <summary>
    /// Whether a token is admitted by row three. Punctuation is admitted wholesale: it is closed by
    /// definition and carries no referent, which is the entire test row three applies.
    /// </summary>
    public bool Admits(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var trimmed = token.Trim();
        return trimmed.Length == 0
            || trimmed.All(c => char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
            || _words.Contains(trimmed)
            || _words.Contains(trimmed.TrimEnd('.', ',', ';', ':', '!', '?'));
    }

    /// <summary>
    /// The crude English lemmatiser the gate ships with: it strips the inflections that change a
    /// word's form without changing which content word it is.
    ///
    /// <para>It is deliberately conservative, and <b>consistency matters more here than accuracy</b>.
    /// The gate compares the rewrite's lemmas against the span's, so a stemmer that maps two forms
    /// of one word to two lemmas produces a false failure, while one that maps an unrelated pair to
    /// the same lemma admits a word the span never used. Both are wrong; only the first is common,
    /// which is why the rules below are the narrow English ones rather than an aggressive stem.</para>
    ///
    /// <para>It does not handle irregular verbs — "ran" and "runs" are different lemmas here — so a
    /// rewrite that re-tenses an irregular verb is rejected and the item falls back to its span.
    /// That is the correct direction to fail in, and where a real lemmatiser exists for a language
    /// it ships beside that language's list (§3.7).</para>
    /// </summary>
    public static string Lemma(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        var value = word.Trim().Trim('"', '\'', '“', '”', '‘', '’', '(', ')', '[', ']', '.', ',', ';', ':', '!', '?')
            .ToLowerInvariant();

        // The possessive first, and always: "james's" and "james" must not lemmatise differently,
        // or a rewrite attaching a name to a noun fails on the name it was given.
        if (value.EndsWith("'s", StringComparison.Ordinal) || value.EndsWith("’s", StringComparison.Ordinal))
        {
            value = value[..^2];
        }

        if (value.Length <= 3)
        {
            return value;
        }

        if (value.Length > 5 && value.EndsWith("ing", StringComparison.Ordinal))
        {
            return value[..^3];
        }

        if (value.Length > 4 && value.EndsWith("ed", StringComparison.Ordinal))
        {
            return value[..^2];
        }

        // "-es" only after a sibilant, which is the English rule: "boxes" is "box", but "James" is
        // not "Jam".
        if (value.Length > 4 && value.EndsWith("es", StringComparison.Ordinal)
            && (value[..^2].EndsWith('s') || value[..^2].EndsWith('x') || value[..^2].EndsWith('z')
                || value[..^2].EndsWith("ch", StringComparison.Ordinal)
                || value[..^2].EndsWith("sh", StringComparison.Ordinal)))
        {
            return value[..^2];
        }

        return value.EndsWith('s') && !value.EndsWith("ss", StringComparison.Ordinal)
            ? value[..^1]
            : value;
    }

    public static IEnumerable<string> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (Match match in TokenPattern().Matches(text))
        {
            yield return match.Value;
        }
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:'[\p{L}]+)?|[^\s\p{L}\p{N}]")]
    private static partial Regex TokenPattern();
}
