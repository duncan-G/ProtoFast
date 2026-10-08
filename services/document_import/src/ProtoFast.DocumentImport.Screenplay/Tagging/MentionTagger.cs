using System.Text;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Tagging;

/// <summary>
/// Turns each full library name in the story's text into an <c>@Name</c> mention, keeping the
/// manuscript's spelling. Characters and locations are proper nouns, so only a capitalised occurrence
/// counts, and a multi-word prop counts in any case. A name that is also an ordinary word is sent to a
/// model, which confirms each occurrence: a one-word prop ("map", "key"), and a character or location
/// the manuscript also writes in lowercase ("Will" beside "will"). Tagging never fails an import:
/// unconfirmed names stay plain text.
/// </summary>
public sealed class MentionTagger(
    ILanguageModelFactory models,
    MentionTaggerOptions options,
    ILogger<MentionTagger> logger)
{
    // The writer cuts longer names, so a longer name would never resolve.
    private const int MaxNameLength = 255;

    private const string System = """
        You check whether words in a story name an entry in its library of characters, locations and
        props. Each candidate is a word spelled like an entry's name, shown in its passage with the word
        in [[double brackets]]. It names the entry when it refers to that character, place or object in
        the story; it does not when the word is used another way ("Will you stay?" when a character is
        called Will, "a key moment" when a prop is a key).

        Reply with one JSON object and nothing else:
        {"references": [the ids of the candidates that name their entry]}
        """;

    public async Task<StoryDraft> TagAsync(StoryDraft draft, CancellationToken ct)
    {
        var names = Library(draft);
        var elements = (draft.Containers ?? [])
            .SelectMany(c => c.Scenes ?? [])
            .SelectMany(s => s.Elements ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Text))
            .ToList();
        var texts = elements.Select(e => e.Text!.Trim()).ToList();

        var found = texts.SelectMany((text, i) => Find(text, i, names)).ToList();
        var lowercase = found.Where(o => o.Name.Kind != MentionKind.Prop && !IsCapitalised(o, texts)).ToList();
        var ordinaryWords = lowercase.Select(o => o.Name).ToHashSet();
        var candidates = found.Except(lowercase).ToList();
        var doubtful = candidates.Where(o => ordinaryWords.Contains(o.Name) || IsOneWordProp(o.Name)).ToList();
        var confirmed = await ConfirmAsync(doubtful, texts, ct);
        var accepted = candidates.Except(doubtful).Concat(confirmed).ToLookup(o => o.Element);

        var tagged = new Dictionary<SceneElementDraft, SceneElementDraft>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < elements.Count; i++)
        {
            var (text, mentions) = Apply(texts[i], accepted[i]);
            tagged[elements[i]] = elements[i] with { Text = text, Mentions = mentions };
        }

        logger.LogInformation(
            "Tagged {Mentions} mentions in {Elements} elements; the model confirmed {Confirmed} of {Doubtful} names that are also ordinary words",
            accepted.Sum(g => g.Count()), elements.Count, confirmed.Count, doubtful.Count);

        return draft with
        {
            Containers = (draft.Containers ?? []).Select(c => c with
            {
                Scenes = (c.Scenes ?? []).Select(s => s with
                {
                    Elements = (s.Elements ?? []).Select(e => tagged.GetValueOrDefault(e, e)).ToList(),
                }).ToList(),
            }).ToList(),
        };
    }

    /// <summary>Longest names first, so "Mara Voss" is matched before "Mara".</summary>
    private static List<LibraryName> Library(StoryDraft draft) =>
        (draft.Characters ?? []).Select(c => new LibraryName(MentionKind.Character, c.Name, c.Description))
            .Concat((draft.Locations ?? []).Select(l => new LibraryName(MentionKind.Location, l.Name, l.Description)))
            .Concat((draft.Props ?? []).Select(p => new LibraryName(MentionKind.Prop, p.Name, p.Description)))
            .Select(n => n with { Name = n.Name?.Trim() ?? "" })
            .Where(n => n.Name.Length is > 0 and <= MaxNameLength)
            .DistinctBy(n => (n.Kind, n.Name.ToUpperInvariant()))
            .OrderByDescending(n => n.Name.Length)
            .ThenBy(n => n.Kind)
            .ToList();

    /// <summary>The editor's <c>matchReference</c> rules: whole words, any case, longest name wins.</summary>
    private static IEnumerable<NameOccurrence> Find(string text, int element, IReadOnlyList<LibraryName> names)
    {
        var at = 0;
        while (at < text.Length)
        {
            var match = at > 0 && (IsWordChar(text[at - 1]) || text[at - 1] == '@')
                ? null
                : names.FirstOrDefault(n => Matches(text, at, n.Name));
            if (match is null)
            {
                at++;
                continue;
            }

            yield return new NameOccurrence(element, at, match.Name.Length, match);
            at += match.Name.Length;
        }
    }

    private static bool Matches(string text, int at, string name)
    {
        var end = at + name.Length;
        return end <= text.Length
            && text.AsSpan(at, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase)
            && (end == text.Length || !IsWordChar(text[end]));
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

    private static bool IsOneWordProp(LibraryName name) =>
        name.Kind == MentionKind.Prop && !name.Name.Any(char.IsWhiteSpace);

    private static bool IsCapitalised(NameOccurrence o, IReadOnlyList<string> texts)
    {
        foreach (var c in texts[o.Element].AsSpan(o.Start, o.Length))
        {
            if (char.IsLetter(c))
            {
                return !char.IsLower(c);
            }
        }

        return true;
    }

    private async Task<List<NameOccurrence>> ConfirmAsync(
        IReadOnlyList<NameOccurrence> doubtful, IReadOnlyList<string> texts, CancellationToken ct)
    {
        var confirmed = new List<NameOccurrence>();
        foreach (var batch in doubtful.Chunk(options.MaxCandidatesPerCall))
        {
            try
            {
                var reply = await models.For(options.ModelClass).CompleteAsync(System, Prompt(batch, texts), ct);
                var ids = StoryJson.Deserialize<MentionJudgement>(StoryJson.ExtractObject(reply.Text)).References ?? [];
                confirmed.AddRange(ids.Where(id => id >= 1 && id <= batch.Length).Distinct().Select(id => batch[id - 1]));
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "Could not confirm {Count} one-word prop mentions; they stay plain text", batch.Length);
            }
        }

        return confirmed;
    }

    private string Prompt(IReadOnlyList<NameOccurrence> batch, IReadOnlyList<string> texts)
    {
        var prompt = new StringBuilder().AppendLine("<library>");
        foreach (var name in batch.Select(o => o.Name).Distinct())
        {
            prompt.AppendLine($"- {Entry(name)}: {name.Description}");
        }

        prompt.AppendLine("</library>").AppendLine().AppendLine("<candidates>");
        for (var i = 0; i < batch.Count; i++)
        {
            prompt.AppendLine($"{i + 1}. {Entry(batch[i].Name)}: {Passage(batch[i], texts[batch[i].Element])}");
        }

        return prompt.AppendLine("</candidates>").ToString();
    }

    private static string Entry(LibraryName name) => $"{name.Name} ({name.Kind.ToString().ToLowerInvariant()})";

    private string Passage(NameOccurrence o, string text)
    {
        var from = Math.Max(0, o.Start - options.ContextChars);
        var end = o.Start + o.Length;
        var to = Math.Min(text.Length, end + options.ContextChars);
        var passage = $"{(from > 0 ? "…" : "")}{text[from..o.Start]}[[{text[o.Start..end]}]]{text[end..to]}{(to < text.Length ? "…" : "")}";
        return passage.ReplaceLineEndings(" ");
    }

    private static (string Text, List<MentionDraft> Mentions) Apply(string text, IEnumerable<NameOccurrence> occurrences)
    {
        var tagged = new StringBuilder(text.Length + 16);
        var mentions = new List<MentionDraft>();
        var at = 0;
        foreach (var o in occurrences.OrderBy(o => o.Start))
        {
            tagged.Append(text, at, o.Start - at);
            mentions.Add(new MentionDraft(o.Name.Kind, o.Name.Name, tagged.Length, o.Length + 1));
            tagged.Append('@').Append(text, o.Start, o.Length);
            at = o.Start + o.Length;
        }

        tagged.Append(text, at, text.Length - at);
        return (tagged.ToString(), mentions);
    }
}
