using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProtoFast.Data.ThePlot;
using ProtoFast.DocumentImport.History;
using ProtoFast.DocumentImport.History.Corpus;
using ProtoFast.DocumentImport.History.Runs;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using ProtoFast.DocumentImport.Screenplay;
using ProtoFast.DocumentImport.Screenplay.Models;
using ProtoFast.DocumentImport.Worker.HistoricalStories;
using ProtoFast.DocumentImport.Worker.Import;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.History;

/// <summary>
/// The history corpus and story runs against real tables. One class, so its runs are claimed one
/// test at a time: a claim takes the oldest queued run of any test.
/// </summary>
[Collection(PlotSchemaCollection.Name)]
public sealed partial class HistoricalStoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // A made-up word keeps this test's book apart from every other's.
    private readonly string _marker = "zq" + string.Concat(Enumerable.Range(0, 10).Select(_ => (char)Random.Shared.Next('a', 'z' + 1)));

    private readonly int _bookId = Random.Shared.Next(1_000_000, int.MaxValue);
    private readonly string _writerId = $"writer-{Guid.NewGuid():N}";
    private readonly HistoryScriptedModels _models = new();
    private ServiceProvider _services = null!;

    private HistoryCorpus Corpus => _services.GetRequiredService<HistoryCorpus>();

    private StoryRunLedger Runs => _services.GetRequiredService<StoryRunLedger>();

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(postgres.DataSource);
        services.AddKeyedSingleton<NpgsqlDataSource>(HistoryDatabase.ConnectionName, postgres.DataSource);
        services.AddThePlotData();
        services.AddScreenplayDiscovery(_ => { });
        services.AddSingleton<ILanguageModelFactory>(_models);
        services.AddScoped<StoryWriter>();
        services.AddHistoricalStories();
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ThePlotDbContext>().Database.MigrateAsync(Ct);
        await HistorySchema.EnsureAsync(postgres.DataSource, Ct);
        await HistorySchema.EnsureAsync(postgres.DataSource, Ct);

        await Corpus.AddBookAsync(
            new CorpusBook(_bookId, "A Ferryman's Recollections", "Doe, Jonah", "2001-01-01", "Boston -- History -- 1775", "F73", "", 1775, 1775),
            [
                "Preface. These recollections were set down in my old age.",
                $"The night was cold and the {_marker} lantern hung in the steeple. Captain Hale told me to row without a sound.",
                "We reached the far shore at midnight, and the regulars' boats lay at anchor under the moon.",
            ],
            Ct);
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task A_book_loads_once_and_its_passages_are_found_by_search_and_read_in_order()
    {
        Assert.False(await Corpus.AddBookAsync(
            new CorpusBook(_bookId, "Again", "", "", "", "", "", null, null), ["Ignored."], Ct));

        var hit = Assert.Single(await Corpus.SearchAsync($"{_marker} steeple", null, null, 5, Ct));
        Assert.Equal((_bookId, 1, "A Ferryman's Recollections"), (hit.BookId, hit.Seq, hit.Title));
        Assert.Contains($"<b>{_marker}</b>", hit.Snippet);

        Assert.Empty(await Corpus.SearchAsync($"{_marker}", fromYear: 1861, toYear: 1865, limit: 5, Ct));
        Assert.Single(await Corpus.SearchAsync($"{_marker}", fromYear: 1770, toYear: 1780, limit: 5, Ct));

        var around = await Corpus.ReadAsync(hit.Id, before: 1, after: 5, Ct);
        Assert.Equal([0, 1, 2], around.Select(p => p.Seq));
        Assert.Empty(await Corpus.ReadAsync(-1, 0, 0, Ct));
    }

    [Fact]
    public async Task A_run_is_claimed_once_beats_while_it_works_and_stops_when_cancelled()
    {
        var id = await Runs.CreateAsync(_writerId, "operator", null, 4, Ct);

        var claimed = await Runs.ClaimAsync(TimeSpan.FromMinutes(2), maxAttempts: 2, Ct);
        Assert.Equal((id, StoryRunStatus.Researching, 1), (claimed?.Id, claimed?.Status, claimed?.Attempts));
        Assert.Null(await Runs.ClaimAsync(TimeSpan.FromMinutes(2), maxAttempts: 2, Ct));

        // A worker that stops beating loses the run to the next claim.
        var retaken = await Runs.ClaimAsync(TimeSpan.Zero, maxAttempts: 2, Ct);
        Assert.Equal((id, 2), (retaken?.Id, retaken?.Attempts));

        await Runs.FailAsync(id, "model down", final: false, Ct);
        Assert.Equal(StoryRunStatus.Queued, (await Runs.GetAsync(id, Ct))!.Status);

        Assert.True(await Runs.CancelAsync(id, Ct));
        Assert.Equal(StoryRunStatus.Cancelled, await Runs.HeartbeatAsync(id, Ct));
        Assert.False(await Runs.CancelAsync(id, Ct));
        await Runs.FinishAsync(id, Guid.NewGuid(), "Too late", Ct);
        Assert.Equal(StoryRunStatus.Cancelled, (await Runs.GetAsync(id, Ct))!.Status);
    }

    [Fact]
    public async Task A_run_researches_the_corpus_writes_a_screenplay_and_saves_it_as_the_writers_story()
    {
        var id = await Runs.CreateAsync(_writerId, "operator", "A ferry crossing under the guns", 2, Ct);
        var run = await Runs.ClaimAsync(TimeSpan.FromMinutes(2), maxAttempts: 2, Ct);
        Assert.Equal(id, run?.Id);

        long found = 0;
        _models.Turns(
            "medium",
            _ => Call(ResearchTools.Search, new { query = $"{_marker} lantern" }),
            messages =>
            {
                found = long.Parse(PassageId().Match(LastResult(messages)).Groups[1].Value);
                return Call(ResearchTools.Read, new { passageId = found });
            },
            _ => Call(ResearchTools.SubmitBrief, Brief(found + 1)),
            messages =>
            {
                Assert.Contains("have not read in full", LastResult(messages));
                return Call(ResearchTools.SubmitBrief, Brief(found));
            });
        _models.Complete("large", user =>
        {
            Assert.Contains("Captain Hale told me to row", user);
            return Screenplay;
        });
        _models.Complete("medium", _ => """{"aliases": []}""");
        _models.Complete("small", _ => """{"references": []}""");

        await using (var scope = _services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<HistoricalStoryRunner>().RunAsync(run!, Ct);
        }

        var finished = await Runs.GetAsync(id, Ct);
        Assert.Equal((StoryRunStatus.Finished, "Crossing"), (finished!.Status, finished.Title));
        Assert.Equal("The Crossing", BriefJson.Deserialize(finished.BriefJson!).Title);

        var events = await Runs.EventsAsync(id, Ct);
        Assert.Equal(["search", "read", "brief", "brief", "draft", "saved"], events.Select(e => e.Kind));
        Assert.Equal(events.Sum(e => e.Cost), finished.Cost);

        await using var check = _services.CreateAsyncScope();
        var story = await check.ServiceProvider.GetRequiredService<ThePlotDbContext>().Stories.IgnoreQueryFilters()
            .Include(s => s.Containers).ThenInclude(c => c.Scenes).ThenInclude(s => s.Elements)
            .Include(s => s.Characters)
            .SingleAsync(s => s.Id == finished.StoryId, Ct);
        Assert.Equal((_writerId, "Crossing"), (story.UserId, story.Title));
        Assert.Equal(["Captain Hale", "Ferryman"], story.Characters.Select(c => c.Name).Order());
        Assert.Equal(2, story.Containers.Single().Scenes.Count);
    }

    private static object Brief(long source) => new
    {
        title = "The Crossing",
        moment = "A ferryman rows a captain past the regulars' boats.",
        when = "1775",
        where = "Boston harbour",
        summary = "They cross unseen.",
        people = new[] { new { name = "Captain Hale", role = "officer", sources = new[] { source } } },
        timeline = new[]
        {
            new { when = "night", what = "The lantern is lit.", sources = new[] { source } },
            new { when = "midnight", what = "They land.", sources = new[] { source } },
        },
        places = Array.Empty<object>(),
        details = Array.Empty<object>(),
        sources = new[] { new { passageId = source, why = "The ferryman's own account." } },
    };

    private const string Screenplay = """
        {
          "title": "Crossing",
          "characters": [{ "name": "Captain Hale", "kind": "Human" }, { "name": "Ferryman", "kind": "Human" }],
          "locations": [{ "name": "OLD NORTH STEEPLE", "setting": "Exterior" }, { "name": "THE RIVER", "setting": "Exterior" }],
          "props": [{ "name": "lantern" }],
          "containers": [{ "label": "Act I", "scenes": [
            { "title": "Signal", "elements": [
              { "type": "Heading", "location": "OLD NORTH STEEPLE", "timeOfDay": "NIGHT" },
              { "type": "Action", "text": "SUPER: Boston. April 1775." },
              { "type": "Action", "text": "A lantern flares high in the steeple." } ] },
            { "title": "Oars", "elements": [
              { "type": "Heading", "location": "THE RIVER", "timeOfDay": "NIGHT" },
              { "type": "Action", "text": "Muffled oars dip. Captain Hale crouches in the bow." },
              { "type": "Dialogue", "speaker": "Captain Hale", "parenthetical": "whispering", "text": "Not a sound." },
              { "type": "Dialogue", "speaker": "Ferryman", "text": "They'll hear your heart before my oars." } ] }
          ] }]
        }
        """;

    private static LanguageModelReply Call(string tool, object input) =>
        new("", "scripted", 100, 50, 0.01m, FinishReason: "tool_call",
            ToolCalls: [new ToolCall(Guid.NewGuid().ToString("N"), tool, JsonSerializer.SerializeToElement(input))]);

    private static string LastResult(IReadOnlyList<ChatMessage> messages) => messages[^1].ToolResults[^1].Content;

    [GeneratedRegex(@"^\[(\d+)\]", RegexOptions.Multiline)]
    private static partial Regex PassageId();
}
