using System.Text;
using System.Text.Json;
using ProtoFast.DocumentImport.History.Corpus;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <summary>The corpus as the research desk sees it: search, read and random draws, plus the brief it submits.</summary>
public sealed class ResearchTools(HistoryCorpus corpus)
{
    public const string Search = "search";
    public const string Read = "read";
    public const string RandomPassages = "random_passages";
    public const string SubmitBrief = "submit_brief";

    private const int DefaultHits = 8;

    private static readonly JsonElement IdList = Schema("""{ "type": "array", "items": { "type": "integer" } }""");

    public static readonly IReadOnlyList<ToolDefinition> Definitions =
    [
        new(Search, "Full-text search over the corpus. Returns the best passages with their ids, books and matching fragments.", Schema("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Words (ANDed), \"quoted phrases\", OR between alternatives, -word to exclude." },
                "fromYear": { "type": "integer", "description": "Only books whose subjects reach this year or later." },
                "toYear": { "type": "integer", "description": "Only books whose subjects start by this year." },
                "limit": { "type": "integer", "description": "1-20, default 8." }
              },
              "required": ["query"]
            }
            """)),
        new(Read, "Reads a passage in full, with up to two neighbouring passages of the same book on each side.", Schema("""
            {
              "type": "object",
              "properties": {
                "passageId": { "type": "integer" },
                "before": { "type": "integer", "description": "Passages before it, 0-2." },
                "after": { "type": "integer", "description": "Passages after it, 0-2." }
              },
              "required": ["passageId"]
            }
            """)),
        new(RandomPassages, "Draws passages in full from random books, one per book, away from front and back matter.", Schema("""
            {
              "type": "object",
              "properties": {
                "count": { "type": "integer", "description": "1-8, default 5." },
                "fromYear": { "type": "integer" },
                "toYear": { "type": "integer" }
              },
              "required": []
            }
            """)),
        new(SubmitBrief, "Submits the research brief. Every cited id must be a passage you have read in full.", Schema($$"""
            {
              "type": "object",
              "properties": {
                "title": { "type": "string", "description": "A working title for the documentary." },
                "moment": { "type": "string", "description": "One sentence: who does what, where and when." },
                "when": { "type": "string", "description": "The date or span, as precisely as the sources allow." },
                "where": { "type": "string" },
                "summary": { "type": "string", "description": "The moment from start to outcome, in a paragraph." },
                "stakes": { "type": "string", "description": "What the people in it stand to win or lose." },
                "background": { "type": "array", "description": "What the narrator needs to set the moment in its time: the larger situation, how it came to this, the forces at work, figures and dates.",
                  "items": { "type": "object", "properties": { "text": { "type": "string" }, "sources": {{IdList}} }, "required": ["text", "sources"] } },
                "aftermath": { "type": "array", "description": "What came of the moment, for the people in it and beyond.",
                  "items": { "type": "object", "properties": { "text": { "type": "string" }, "sources": {{IdList}} }, "required": ["text", "sources"] } },
                "people": { "type": "array", "items": { "type": "object", "properties": {
                  "name": { "type": "string" }, "role": { "type": "string" }, "description": { "type": "string" },
                  "facts": { "type": "array", "items": { "type": "string" } }, "sources": {{IdList}} },
                  "required": ["name", "role", "sources"] } },
                "timeline": { "type": "array", "items": { "type": "object", "properties": {
                  "when": { "type": "string" }, "what": { "type": "string" }, "sources": {{IdList}} },
                  "required": ["when", "what", "sources"] } },
                "places": { "type": "array", "items": { "type": "object", "properties": {
                  "name": { "type": "string" }, "description": { "type": "string" }, "sources": {{IdList}} },
                  "required": ["name", "description", "sources"] } },
                "details": { "type": "array", "items": { "type": "object", "properties": {
                  "text": { "type": "string" }, "sources": {{IdList}} }, "required": ["text", "sources"] } },
                "quotes": { "type": "array", "items": { "type": "object", "properties": {
                  "speaker": { "type": "string" }, "words": { "type": "string", "description": "Exactly as the source gives them." },
                  "context": { "type": "string" }, "source": { "type": "integer" } },
                  "required": ["speaker", "words", "source"] } },
                "sources": { "type": "array", "items": { "type": "object", "properties": {
                  "passageId": { "type": "integer" }, "why": { "type": "string" } }, "required": ["passageId", "why"] } }
              },
              "required": ["title", "moment", "when", "where", "summary", "background", "aftermath", "people", "timeline", "places", "details", "sources"]
            }
            """)),
    ];

    /// <summary>Passages the desk has seen in full, which are the ones it may cite.</summary>
    public HashSet<long> ReadPassages { get; } = [];

    public async Task<ToolOutcome> RunAsync(ToolCall call, CancellationToken ct)
    {
        var input = call.Input;
        switch (call.Name)
        {
            case Search:
            {
                var query = Text(input, "query") ?? "";
                if (query.Trim().Length == 0)
                {
                    return ToolOutcome.Error("search needs a query.");
                }

                var (fromYear, toYear, limit) = (Int(input, "fromYear"), Int(input, "toYear"), Math.Clamp(Int(input, "limit") ?? DefaultHits, 1, 20));
                var hits = await corpus.SearchAsync(query, fromYear, toYear, limit, ct);
                var text = new StringBuilder();
                var loose = hits.Count == 0 ? AnyWord(query) : null;
                if (loose is not null)
                {
                    hits = await corpus.SearchAsync(loose, fromYear, toYear, limit, ct);
                    text.AppendLine(hits.Count == 0
                        ? $"No passages match {query}, or any of its words."
                        : $"No passage has every word of {query}; {hits.Count} passages with some of them, best first:");
                }
                else
                {
                    text.AppendLine(hits.Count == 0 ? $"No passages match {query}." : $"{hits.Count} passages for {query}:");
                }

                foreach (var hit in hits)
                {
                    text.AppendLine().AppendLine($"[{hit.Id}] {Book(hit.Title, hit.Authors, hit.FirstYear, hit.LastYear)}, passage {hit.Seq + 1}");
                    text.AppendLine($"    {hit.Snippet}");
                }

                return new ToolOutcome(text.ToString(), false, $"search {query} ({hits.Count}{(loose is null ? "" : " with any word")})");
            }

            case Read:
            {
                if (Long(input, "passageId") is not { } id)
                {
                    return ToolOutcome.Error("read needs a passageId.");
                }

                var passages = await corpus.ReadAsync(id, Int(input, "before") ?? 0, Int(input, "after") ?? 0, ct);
                if (passages.Count == 0)
                {
                    return ToolOutcome.Error($"There is no passage {id}.");
                }

                var target = passages.First(p => p.Id == id);
                return new ToolOutcome(Passages(passages), false, $"read [{id}] {target.Title}, passage {target.Seq + 1}" +
                    (passages.Count > 1 ? $" (+{passages.Count - 1} around it)" : ""));
            }

            case RandomPassages:
            {
                var passages = await corpus.RandomAsync(
                    Math.Clamp(Int(input, "count") ?? 5, 1, 8), Int(input, "fromYear"), Int(input, "toYear"), ct);
                return passages.Count == 0
                    ? ToolOutcome.Error("The corpus is empty: no books have been loaded.")
                    : new ToolOutcome(Passages(passages), false, $"random passages from {string.Join("; ", passages.Select(p => p.Title))}");
            }

            default:
                return ToolOutcome.Error($"There is no tool named {call.Name}.");
        }
    }

    private string Passages(IReadOnlyList<CorpusPassage> passages)
    {
        var text = new StringBuilder();
        foreach (var passage in passages)
        {
            ReadPassages.Add(passage.Id);
            text.AppendLine($"[{passage.Id}] {Book(passage.Title, passage.Authors, passage.FirstYear, passage.LastYear)}, passage {passage.Seq + 1}");
            text.AppendLine(passage.Text).AppendLine();
        }

        return text.ToString();
    }

    /// <summary>The query's plain words ORed, for a search that found nothing; null when it is one word or uses the syntax itself.</summary>
    internal static string? AnyWord(string query)
    {
        if (query.Contains('"') || query.Contains(" OR ", StringComparison.Ordinal) || query.Contains('-'))
        {
            return null;
        }

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length < 2 ? null : string.Join(" OR ", words);
    }

    public static string Book(string title, string authors, int? firstYear, int? lastYear)
    {
        var years = firstYear is { } first ? first == lastYear ? $" ({first})" : $" ({first}-{lastYear})" : "";
        return authors.Length > 0 ? $"\"{title}\" by {authors}{years}" : $"\"{title}\"{years}";
    }

    private static string? Text(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(v.GetString()?.Trim('[', ']', '#', ' '), out var n) => n,
            _ => null,
        } : null;

    private static int? Int(JsonElement input, string name) => Long(input, name) is { } n ? (int)Math.Clamp(n, int.MinValue, int.MaxValue) : null;

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
