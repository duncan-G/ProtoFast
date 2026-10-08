using System.Text;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Tagging;

/// <summary>
/// Turns each full library name in the story's text into an <c>@Name</c> mention, keeping the
/// manuscript's spelling, and tags the other names the manuscript uses for an entry ("John",
/// "Mr. Smith" or "Joe" for John Smith) as they are written. Aliases are found by offering a model
/// the manuscript's capitalised phrases to link to library entries. Characters, locations and aliases
/// are proper nouns, so only a capitalised occurrence counts, and a multi-word prop counts in any
/// case. A name that is also an ordinary word is sent to a model, which confirms each occurrence: a
/// one-word prop ("map", "key"), and a character or location the manuscript also writes in lowercase
/// ("Will" beside "will"). Tagging never fails an import: unconfirmed names stay plain text.
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

    private const string AliasSystem = """
        You find the other names a story uses for the entries in its library of characters, locations
        and props. An alias is another way the manuscript refers to the same entry: part of its name
        ("John" or "Smith" for John Smith), a title with a name ("Mr. Smith", "Captain Reyes"), a
        nickname ("Joe", "Red"), or another name for a place or object ("the Keep" for Harrenhal Keep).

        Each candidate is a capitalised phrase from the manuscript, shown with how often it occurs and
        passages with the phrase in [[double brackets]]. Link a candidate to the entry the manuscript
        uses it for. Leave it out when it names someone or something outside the library, when it is an
        ordinary word, or when it could mean more than one entry (a surname two characters share).

        Reply with one JSON object and nothing else:
        {"aliases": [{"candidate": a candidate id, "entry": the id of the entry it names}]}
        """;

    // Abbreviated titles keep their period inside a phrase, so "Mr. Smith" is one candidate.
    private static readonly HashSet<string> Titles = new(
        ["Mr", "Mrs", "Ms", "Mx", "Dr", "Prof", "Sr", "Jr", "St", "Capt", "Col", "Gen", "Lt", "Sgt", "Rev", "Fr"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<StoryDraft> TagAsync(StoryDraft draft, CancellationToken ct)
    {
        var elements = (draft.Containers ?? [])
            .SelectMany(c => c.Scenes ?? [])
            .SelectMany(s => s.Elements ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Text))
            .ToList();
        var texts = elements.Select(e => e.Text!.Trim()).ToList();

        var library = Library(draft);
        var aliases = await AliasesAsync(library, texts, ct);
        var names = library.Concat(aliases)
            .OrderByDescending(n => n.Spelling.Length)
            .ThenBy(n => n.Kind)
            .ToList();

        var found = texts.SelectMany((text, i) => Find(text, i, names)).ToList();
        var lowercase = found.Where(o => IsProperNoun(o.Name) && !IsCapitalised(o, texts)).ToList();
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
            "Tagged {Mentions} mentions in {Elements} elements using {Aliases} aliases; the model confirmed {Confirmed} of {Doubtful} names that are also ordinary words",
            accepted.Sum(g => g.Count()), elements.Count, aliases.Count, confirmed.Count, doubtful.Count);

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

    private static List<LibraryName> Library(StoryDraft draft) =>
        (draft.Characters ?? []).Select(c => new LibraryName(MentionKind.Character, c.Name, c.Description))
            .Concat((draft.Locations ?? []).Select(l => new LibraryName(MentionKind.Location, l.Name, l.Description)))
            .Concat((draft.Props ?? []).Select(p => new LibraryName(MentionKind.Prop, p.Name, p.Description)))
            .Select(n => n with { Name = n.Name?.Trim() ?? "" })
            .Where(n => n.Name.Length is > 0 and <= MaxNameLength)
            .DistinctBy(n => (n.Kind, n.Name.ToUpperInvariant()))
            .ToList();

    /// <summary>
    /// The manuscript's capitalised phrases a model links to exactly one entry. A library name is never
    /// offered, nor a one-word phrase that only starts sentences or that the manuscript also writes in
    /// lowercase.
    /// </summary>
    private async Task<List<LibraryName>> AliasesAsync(
        IReadOnlyList<LibraryName> library, IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (library.Count == 0)
        {
            return [];
        }

        var candidates = AliasCandidates(library, texts);
        var links = new List<(AliasCandidate Candidate, LibraryName Entry)>();
        foreach (var batch in candidates.Chunk(options.MaxCandidatesPerCall))
        {
            try
            {
                var reply = await models.For(options.AliasModelClass).CompleteAsync(AliasSystem, AliasPrompt(library, batch, texts), ct);
                var judged = StoryJson.Deserialize<AliasJudgement>(StoryJson.ExtractObject(reply.Text)).Aliases ?? [];
                links.AddRange(judged
                    .Where(l => l.Candidate >= 1 && l.Candidate <= batch.Length && l.Entry >= 1 && l.Entry <= library.Count)
                    .Select(l => (batch[l.Candidate - 1], library[l.Entry - 1])));
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "Could not find aliases among {Count} capitalised phrases; they stay plain text", batch.Length);
            }
        }

        return links
            .Distinct()
            .GroupBy(l => l.Candidate)
            .Where(g => g.Count() == 1)
            .Select(g => g.Single())
            .Select(l => l.Entry with { Alias = l.Candidate.Spelling })
            .ToList();
    }

    private List<AliasCandidate> AliasCandidates(IReadOnlyList<LibraryName> library, IReadOnlyList<string> texts)
    {
        var names = library.Select(n => n.Name).ToHashSet(LibraryNameComparer.Instance);
        var ordinaryWords = texts
            .SelectMany(Words)
            .Select(w => w.Word)
            .Where(w => char.IsLower(w[0]))
            .ToHashSet(StringComparer.Ordinal);

        var phrases = texts.SelectMany((text, i) => Phrases(text, ordinaryWords).Select(p => (Element: i, p.Start, p.Length, p.MidSentence)));
        return phrases
            .GroupBy(p => texts[p.Element].Substring(p.Start, p.Length), LibraryNameComparer.Instance)
            .Where(g => !names.Contains(g.Key) && !Titles.Contains(g.Key))
            .Where(g => g.Key.Contains(' ') || (g.Any(p => p.MidSentence) && !ordinaryWords.Contains(g.Key.ToLowerInvariant())))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(options.MaxAliasCandidates)
            .Select(g =>
            {
                var samples = g.OrderByDescending(p => p.MidSentence)
                    .Take(options.AliasSamples)
                    .Select(p => (p.Element, p.Start))
                    .ToList();
                var (element, start) = samples[0];
                return new AliasCandidate(texts[element].Substring(start, g.Key.Length), g.Count(), samples);
            })
            .ToList();
    }

    /// <summary>
    /// Runs of capitalised words, joined by single spaces or a title's period. At a sentence start,
    /// leading words the manuscript also writes in lowercase are dropped, so "Then John" yields "John".
    /// </summary>
    private static IEnumerable<(int Start, int Length, bool MidSentence)> Phrases(string text, IReadOnlySet<string> ordinaryWords)
    {
        var words = Words(text).ToList();
        for (var i = 0; i < words.Count; i++)
        {
            if (!char.IsUpper(words[i].Word[0]))
            {
                continue;
            }

            var first = i;
            while (i + 1 < words.Count && char.IsUpper(words[i + 1].Word[0])
                   && Joins(text, words[i].Start + words[i].Word.Length, words[i + 1].Start, words[i].Word))
            {
                i++;
            }

            var midSentence = !IsSentenceStart(text, words[first].Start);
            if (!midSentence)
            {
                while (first < i && ordinaryWords.Contains(words[first].Word.ToLowerInvariant()))
                {
                    first++;
                    midSentence = true;
                }
            }

            var start = words[first].Start;
            yield return (start, words[i].Start + words[i].Word.Length - start, midSentence);
        }
    }

    private static bool Joins(string text, int end, int next, string word)
    {
        var gap = text.AsSpan(end, next - end);
        return gap is " " || (gap is ". " && Titles.Contains(word));
    }

    /// <summary>Letters and digits, with an inner hyphen or an apostrophe before a capital ("O'Brien"), but not a possessive.</summary>
    private static IEnumerable<(int Start, string Word)> Words(string text)
    {
        var at = 0;
        while (at < text.Length)
        {
            if (!char.IsLetter(text[at]) || (at > 0 && IsWordChar(text[at - 1])))
            {
                at++;
                continue;
            }

            var end = at;
            while (end < text.Length)
            {
                if (IsWordChar(text[end]))
                {
                    end++;
                }
                else if (end + 1 < text.Length && IsWordChar(text[end - 1])
                         && ((text[end] == '-' && char.IsLetter(text[end + 1]))
                             || (text[end] is '\'' or '’' && char.IsUpper(text[end + 1]))))
                {
                    end += 2;
                }
                else
                {
                    break;
                }
            }

            yield return (at, text[at..end]);
            at = end;
        }
    }

    private static bool IsSentenceStart(string text, int start)
    {
        var at = start - 1;
        while (at >= 0 && (char.IsWhiteSpace(text[at]) || text[at] is '"' or '“' or '‘' or '\'' or '(' or '[' or '-' or '—' or '–'))
        {
            at--;
        }

        return at < 0 || text[at] is '.' or '!' or '?' or '…' or ':' or ';';
    }

    private string AliasPrompt(IReadOnlyList<LibraryName> library, IReadOnlyList<AliasCandidate> batch, IReadOnlyList<string> texts)
    {
        var prompt = new StringBuilder().AppendLine("<library>");
        for (var i = 0; i < library.Count; i++)
        {
            prompt.AppendLine($"{i + 1}. {Entry(library[i])}: {library[i].Description}");
        }

        prompt.AppendLine("</library>").AppendLine().AppendLine("<candidates>");
        for (var i = 0; i < batch.Count; i++)
        {
            var times = batch[i].Count == 1 ? "once" : $"{batch[i].Count} times";
            prompt.AppendLine($"{i + 1}. {batch[i].Spelling} ({times})");
            foreach (var (element, start) in batch[i].Samples)
            {
                prompt.AppendLine($"   - {Passage(texts[element], start, batch[i].Spelling.Length)}");
            }
        }

        return prompt.AppendLine("</candidates>").ToString();
    }

    /// <summary>The editor's <c>matchReference</c> rules: whole words, any case, longest name wins.</summary>
    private static IEnumerable<NameOccurrence> Find(string text, int element, IReadOnlyList<LibraryName> names)
    {
        var at = 0;
        while (at < text.Length)
        {
            var match = at > 0 && (IsWordChar(text[at - 1]) || text[at - 1] == '@')
                ? null
                : names.FirstOrDefault(n => Matches(text, at, n.Spelling));
            if (match is null)
            {
                at++;
                continue;
            }

            yield return new NameOccurrence(element, at, match.Spelling.Length, match);
            at += match.Spelling.Length;
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

    private static bool IsProperNoun(LibraryName name) => name.Kind != MentionKind.Prop || name.Alias is not null;

    private static bool IsOneWordProp(LibraryName name) =>
        name is { Kind: MentionKind.Prop, Alias: null } && !name.Name.Any(char.IsWhiteSpace);

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
                logger.LogWarning(e, "Could not confirm {Count} mentions of names that are also ordinary words; they stay plain text", batch.Length);
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
            prompt.AppendLine($"{i + 1}. {Entry(batch[i].Name)}: {Passage(texts[batch[i].Element], batch[i].Start, batch[i].Length)}");
        }

        return prompt.AppendLine("</candidates>").ToString();
    }

    private static string Entry(LibraryName name)
    {
        var kind = name.Kind.ToString().ToLowerInvariant();
        return name.Alias is null ? $"{name.Name} ({kind})" : $"{name.Alias} ({kind}, another name for {name.Name})";
    }

    private string Passage(string text, int start, int length)
    {
        var from = Math.Max(0, start - options.ContextChars);
        var end = start + length;
        var to = Math.Min(text.Length, end + options.ContextChars);
        var passage = $"{(from > 0 ? "…" : "")}{text[from..start]}[[{text[start..end]}]]{text[end..to]}{(to < text.Length ? "…" : "")}";
        return passage.ReplaceLineEndings(" ");
    }

    /// <summary>A name becomes <c>@Name</c>; an alias stays as written and is tagged instead.</summary>
    private static (string Text, List<MentionDraft> Mentions) Apply(string text, IEnumerable<NameOccurrence> occurrences)
    {
        var tagged = new StringBuilder(text.Length + 16);
        var mentions = new List<MentionDraft>();
        var at = 0;
        foreach (var o in occurrences.OrderBy(o => o.Start))
        {
            tagged.Append(text, at, o.Start - at);
            if (o.Name.Alias is null)
            {
                mentions.Add(new MentionDraft(o.Name.Kind, o.Name.Name, tagged.Length, o.Length + 1));
                tagged.Append('@');
            }
            else
            {
                mentions.Add(new MentionDraft(o.Name.Kind, o.Name.Name, tagged.Length, o.Length, IsTag: true));
            }

            tagged.Append(text, o.Start, o.Length);
            at = o.Start + o.Length;
        }

        tagged.Append(text, at, text.Length - at);
        return (tagged.ToString(), mentions);
    }
}
