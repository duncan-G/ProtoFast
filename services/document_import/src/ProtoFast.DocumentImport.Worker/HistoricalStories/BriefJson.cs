using System.Text.Json;
using System.Text.Json.Serialization;
using ProtoFast.DocumentImport.Screenplay.Agents;

namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

public static class BriefJson
{
    private const int MinBackground = 3;

    // Models sometimes quote passage ids.
    public static readonly JsonSerializerOptions Options = new(StoryJson.Options)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string Serialize(ResearchBrief brief) => JsonSerializer.Serialize(brief, Options);

    public static ResearchBrief Deserialize(JsonElement json) =>
        json.Deserialize<ResearchBrief>(Options) ?? throw new JsonException("The brief is null.");

    public static ResearchBrief Deserialize(string json) =>
        JsonSerializer.Deserialize<ResearchBrief>(json, Options) ?? throw new JsonException("The brief is null.");

    /// <summary>What keeps the brief from being written from; empty when it is ready.</summary>
    public static IReadOnlyList<string> Problems(ResearchBrief brief, IReadOnlySet<long> read)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(brief.Title) || string.IsNullOrWhiteSpace(brief.Moment) || string.IsNullOrWhiteSpace(brief.Summary))
        {
            problems.Add("The brief needs a title, a moment and a summary.");
        }

        if ((brief.Background?.Count ?? 0) < MinBackground)
        {
            problems.Add($"The narrator needs at least {MinBackground} background facts: the larger situation, how it came to this, the figures and dates.");
        }

        if (brief.People is not { Count: > 0 })
        {
            problems.Add("The brief names no people.");
        }

        if (brief.Timeline is not { Count: >= 2 })
        {
            problems.Add("The timeline needs at least two events.");
        }

        if (brief.Sources is not { Count: > 0 })
        {
            problems.Add("The brief lists no sources.");
        }

        var unread = brief.CitedPassages().Where(id => !read.Contains(id)).ToList();
        if (unread.Count > 0)
        {
            problems.Add($"It cites passages you have not read in full: {string.Join(", ", unread)}. Read them, or drop the citations and anything resting only on them.");
        }

        return problems;
    }
}
