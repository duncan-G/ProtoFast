using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class NoteTests(StoryDatabase database)
{
    private readonly Seeker _seeker = new(database);
    private readonly Seeker _stranger = new(database);

    [Fact]
    public async Task A_note_keeps_its_markdown_indentation_and_lists()
    {
        var note = new Note
        {
            Kind = NoteKind.Pattern,
            Title = " Monotonic stack ",
            Tag = "Stack",
            Body = "\n    st = []\n    for i, x in enumerate(a):\n        st.append(i)\n\n$O(n)$\n\n",
        };
        note.Badges.Add("O(n) time");
        note.Fits.AddRange(["Daily Temperatures", ""]);

        await _seeker.CreateNoteAsync(note);

        var stored = Assert.Single(await _seeker.ListNotesAsync());
        Assert.Equal("Monotonic stack", stored.Title);
        Assert.Equal("    st = []\n    for i, x in enumerate(a):\n        st.append(i)\n\n$O(n)$", stored.Body);
        Assert.Equal(["O(n) time"], stored.Badges);
        Assert.Equal(["Daily Temperatures"], stored.Fits);
    }

    [Fact]
    public async Task A_company_note_belongs_to_one_of_your_own_companies()
    {
        var ours = await _seeker.CreateCompanyAsync("Halcyon Pay");
        var theirs = await _stranger.CreateCompanyAsync("Quarry Systems");
        var note = await _seeker.CreateNoteAsync(
            new Note { Kind = NoteKind.Company, CompanyId = ours.Id, Title = "Onsite — the four rounds" });
        var moved = note.Clone();
        moved.CompanyId = theirs.Id;

        Task[] attempts =
        [
            _seeker.CreateNoteAsync(new Note { Kind = NoteKind.Company, Title = "Screen" }),
            _seeker.CreateNoteAsync(new Note { Kind = NoteKind.Company, CompanyId = theirs.Id, Title = "Screen" }),
            _seeker.CreateNoteAsync(new Note { Kind = NoteKind.Story, CompanyId = ours.Id, Title = "Screen" }),
            _seeker.UpdateNoteAsync(moved),
        ];

        foreach (var attempt in attempts)
        {
            var error = await Assert.ThrowsAsync<RpcException>(() => attempt);
            Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        }

        var stored = Assert.Single(await _seeker.ListNotesAsync());
        Assert.Equal(NoteKind.Company, stored.Kind);
        Assert.Equal(ours.Id, stored.CompanyId);
    }

    [Fact]
    public async Task Imported_stories_are_saved_in_the_order_sent()
    {
        var first = Story("Billing cron → queue migration", "ownership");
        first.Fits.Add("Led something end to end");

        var imported = await _seeker.ImportStoriesAsync(first, Story("Postgres over DynamoDB", "tradeoffs"));

        Assert.Equal(["Billing cron → queue migration", "Postgres over DynamoDB"], imported.Select(n => n.Title));
        Assert.All(imported, story => Assert.NotEmpty(story.Id));
        Assert.Equal(["Led something end to end"], imported[0].Fits);
        Assert.Equal(2, (await _seeker.ListNotesAsync()).Count);
        Assert.Empty(await _stranger.ListNotesAsync());
    }

    [Fact]
    public async Task One_bad_story_stops_the_whole_import()
    {
        var tooLong = Story(new string('x', 201), "impact");
        var pattern = Story("Monotonic stack", "stack");
        pattern.Kind = NoteKind.Pattern;

        var invalid = await Assert.ThrowsAsync<RpcException>(
            () => _seeker.ImportStoriesAsync(Story("Fine", "impact"), tooLong));
        var notAStory = await Assert.ThrowsAsync<RpcException>(() => _seeker.ImportStoriesAsync(pattern));
        var empty = await Assert.ThrowsAsync<RpcException>(() => _seeker.ImportStoriesAsync());
        var tooMany = await Assert.ThrowsAsync<RpcException>(
            () => _seeker.ImportStoriesAsync([.. Enumerable.Range(1, 51).Select(i => Story($"Story {i}", "impact"))]));

        Assert.StartsWith("Story 2:", invalid.Status.Detail);
        Assert.All([invalid, notAStory, empty, tooMany], e => Assert.Equal(StatusCode.InvalidArgument, e.StatusCode));
        Assert.Empty(await _seeker.ListNotesAsync());
    }

    private static Note Story(string title, string tag) =>
        new() { Kind = NoteKind.Story, Title = title, Tag = tag, Body = "Situation: …" };
}
