using System.Text.RegularExpressions;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Items;

/// <summary>
/// One proposed cut inside a paragraph: the offset the item starts at, and what kind it is.
///
/// <para>A <em>cut point</em> rather than a span, and that choice is the whole reason
/// <c>item-coverage</c> and <c>display-integrity</c> cannot fail at phase 8. A model that returns
/// spans can return spans that overlap or leave gaps; a model that returns cut points cannot,
/// because code builds the spans between them. It is the same discipline as
/// <c>OrchestratedTreeMaterializer</c>: the model composes references, code materializes, and the
/// invariant becomes a property rather than a check.</para>
/// </summary>
public sealed record ItemCut(
    int StartOffset,
    ItemKind Kind,
    SpeechAttributes? Speech = null,
    bool IsStandalone = true,
    string? Certainty = null);

/// <summary>
/// The deterministic pre-segmentation of phase 8 (scene plan §8.6): quoted dialogue, speaker
/// labels, fenced blocks and tables, so the model sees only genuinely ambiguous prose.
///
/// <para>This is the second of the three things holding tagging cost down (§4.5) applied to item
/// typing: every span this class settles is a span nothing is asked about.</para>
/// </summary>
public static partial class ItemSegmenter
{
    /// <summary>
    /// Cuts a paragraph as far as code can. The result always starts at offset 0 and is ordered,
    /// so it is a complete partition on its own — an agent refines it and never has to complete it.
    /// </summary>
    public static IReadOnlyList<ItemCut> PreSegment(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var text = paragraph.Text;

        if (text.Length == 0)
        {
            return [];
        }

        // An opaque block is one item by definition: the pipeline deliberately does not interpret
        // tables, code or equations, and cutting inside one would claim it had (plan §12.2).
        if (paragraph.Kind is ParagraphKind.Table or ParagraphKind.Code or ParagraphKind.Equation)
        {
            return [new ItemCut(0, ItemKind.Exhibit)];
        }

        if (paragraph.Kind == ParagraphKind.Caption)
        {
            return [new ItemCut(0, ItemKind.Exhibit)];
        }

        if (SpeakerLabelPattern().Match(text) is { Success: true } label)
        {
            // A speaker-labelled turn is the case the transcript and screenplay families are made
            // of, and it resolves with no model at all: the label IS the surface form, and phase 9
            // binds it deterministically (§8.4).
            var speaker = label.Groups["speaker"].Value.Trim();
            return
            [
                new ItemCut(0, ItemKind.Speech, SpeechAttributes.Dialogue(speaker)),
            ];
        }

        if (IsTransition(text))
        {
            return [new ItemCut(0, ItemKind.Transition)];
        }

        var cuts = QuotedDialogue(text);

        return cuts.Count > 0 ? cuts : [new ItemCut(0, DefaultKind(text))];
    }

    /// <summary>
    /// True when a paragraph carries no judgement call worth a model call — an opaque block, a
    /// speaker-labelled turn, or prose with no quotation in it at all. Those are handed straight
    /// to the materializer.
    /// </summary>
    public static bool IsSettled(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        return paragraph.Kind is ParagraphKind.Table or ParagraphKind.Code
                or ParagraphKind.Equation or ParagraphKind.Caption
            || SpeakerLabelPattern().IsMatch(paragraph.Text);
    }

    /// <summary>
    /// Cuts around quotation marks. The attribution clause belongs to the <see cref="ItemKind.Speech"/>
    /// item it attributes (§3.3), so <c>James shouted, "Bye" as he ran for the door.</c> partitions
    /// as <c>James shouted, "Bye"</c> and <c> as he ran for the door.</c> — adjacent, no gap, no
    /// overlap, and that is also where <c>SpeechAttributes.SurfaceForm</c> is read from.
    /// </summary>
    internal static IReadOnlyList<ItemCut> QuotedDialogue(string text)
    {
        var quotes = QuoteRuns(text);
        if (quotes.Count == 0)
        {
            return [];
        }

        var sentences = SentenceSplitter.Spans(text);
        var cuts = new List<ItemCut>();
        var cursor = 0;

        foreach (var (quoteStart, quoteEnd) in quotes)
        {
            var sentence = sentences.FirstOrDefault(s => quoteStart >= s.Start && quoteStart < s.End);
            var speechStart = Math.Max(cursor, sentence == default ? quoteStart : sentence.Start);
            var speechEnd = ExtendThroughTrailingAttribution(text, quoteEnd);

            if (speechStart > cursor)
            {
                // Prose before the attribution in the same sentence. It is an event or a state, and
                // which of the two is the one distinction nothing load-bearing depends on (§3.1).
                cuts.Add(new ItemCut(cursor, DefaultKind(text[cursor..speechStart]), IsStandalone: cursor == 0));
            }

            cuts.Add(new ItemCut(
                speechStart,
                ItemKind.Speech,
                SpeechAttributes.Dialogue(AttributionSurfaceForm(text[speechStart..speechEnd]))));

            cursor = speechEnd;
        }

        if (cursor < text.Length)
        {
            // The tail after a quote is the archetypal non-standalone fragment: " as he ran for the
            // door." is a subordinate clause with an unresolved pronoun, and it is exactly what the
            // render-text augmentation exists for (§3.6).
            cuts.Add(new ItemCut(cursor, DefaultKind(text[cursor..]), IsStandalone: false));
        }

        return cuts;
    }

    /// <summary>
    /// Paired quotation runs, straight and curly. Unpaired marks are ignored rather than guessed
    /// at — an apostrophe is not a quotation, and a document with one stray mark must not have
    /// every following paragraph read as speech.
    /// </summary>
    private static IReadOnlyList<(int Start, int End)> QuoteRuns(string text)
    {
        var runs = new List<(int Start, int End)>();
        var open = -1;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '“':
                    open = i;
                    break;
                case '”' when open >= 0:
                    runs.Add((open, i + 1));
                    open = -1;
                    break;
                case '"' when open >= 0:
                    runs.Add((open, i + 1));
                    open = -1;
                    break;
                case '"':
                    open = i;
                    break;
            }
        }

        return runs;
    }

    /// <summary>
    /// <c>"Bye," James shouted.</c> — the attribution after the quote belongs to the speech item
    /// too (§3.3). It stops at the attribution verb, so <c>"Bye," James shouted as he ran for the
    /// door.</c> is a Speech item and an Action item rather than one Speech item swallowing the
    /// action — which is the same partition the plan's worked example produces with the attribution
    /// on the other side.
    /// </summary>
    private static int ExtendThroughTrailingAttribution(string text, int quoteEnd)
    {
        // \G, not ^: Match(text, startat) does not move where ^ matches, so an anchored pattern
        // silently never fires and every quoted utterance loses its attribution — and with it the
        // speaker surface form that phase 9 binds.
        var match = TrailingAttributionPattern().Match(text, quoteEnd);
        return match.Success && match.Index == quoteEnd ? quoteEnd + match.Length : quoteEnd;
    }

    /// <summary>
    /// The speaker as written, from the attribution clause. Empty when the span carries none, which
    /// phase 9 resolves as an unattributed utterance rather than guessing a name (C11).
    /// </summary>
    internal static string AttributionSurfaceForm(string speechSpan)
    {
        var match = AttributionNamePattern().Match(speechSpan);
        return match.Success ? match.Groups["speaker"].Value.Trim() : string.Empty;
    }

    /// <summary>
    /// Event or state. A past-tense finite verb leans <see cref="ItemKind.Action"/>; anything else
    /// is <see cref="ItemKind.Description"/>. The pair is genuinely ambiguous on many spans and
    /// this is deliberately a lean rather than a decision — no hard gate reads the distinction and
    /// mode derivation is built not to be sensitive to it (§3.1, §8.3).
    /// </summary>
    internal static ItemKind DefaultKind(string span) =>
        ActionVerbPattern().IsMatch(span) ? ItemKind.Action : ItemKind.Description;

    /// <summary>
    /// A short paragraph that marks the shift into what follows. Screenplay slug lines are included
    /// because they are the one family where transitions are always marked — which is exactly why
    /// the <c>MinSceneSpan</c> floor is 1 there and never runs (§8.8).
    /// </summary>
    internal static bool IsTransition(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 80
            && (SlugLinePattern().IsMatch(trimmed) || TransitionPhrasePattern().IsMatch(trimmed));
    }

    [GeneratedRegex(@"^\s*(?<speaker>[\p{Lu}][\p{L}\p{N}.\-' ]{0,38}):\s")]
    private static partial Regex SpeakerLabelPattern();

    [GeneratedRegex(@"^\s*(INT\.|EXT\.|INT/EXT\.|I/E\.)|^\s*(FADE (IN|OUT)|CUT TO|DISSOLVE TO|SMASH CUT)\b")]
    private static partial Regex SlugLinePattern();

    [GeneratedRegex(
        @"^(meanwhile|later that (day|night|evening|morning|week)|the next (day|morning|evening)|"
        + @"(three|two|four|five|six|seven|eight|nine|ten|\d+) (minutes|hours|days|weeks|months|years) "
        + @"(later|earlier|before|ago)|elsewhere|that (evening|night|afternoon|morning)|"
        + @"back (in|at) )",
        RegexOptions.IgnoreCase)]
    private static partial Regex TransitionPhrasePattern();

    [GeneratedRegex(@"\G\s*[,.]?\s*(?<speaker>[\p{Lu}][\p{L}\-']*(\s+[\p{Lu}][\p{L}\-']*)?)\s+"
        + @"(said|says|asked|asks|replied|replies|shouted|shouts|whispered|whispers|answered|answers|"
        + @"muttered|mutters|called|calls|added|adds|continued|continues)\b(\s+\w+ly)?[.!?]?")]
    private static partial Regex TrailingAttributionPattern();

    [GeneratedRegex(@"(?<speaker>[\p{Lu}][\p{L}\-']*(\s+[\p{Lu}][\p{L}\-']*)?)\s+"
        + @"(said|says|asked|asks|replied|replies|shouted|shouts|whispered|whispers|answered|answers|"
        + @"muttered|mutters|called|calls|added|adds|continued|continues)\b")]
    private static partial Regex AttributionNamePattern();

    [GeneratedRegex(@"\b\w+(ed|ing)\b\s|\b(ran|went|came|took|struck|threw|opened|closed|turned|rose|fell|"
        + @"stood|sat|walked|entered|left|grabbed|pushed|pulled)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ActionVerbPattern();
}
