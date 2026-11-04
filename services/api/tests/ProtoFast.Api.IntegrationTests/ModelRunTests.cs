using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Api.Services.OffTheLoop;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class ModelRunTests(StoryDatabase database)
{
    private readonly Seeker _seeker = new(database);
    private readonly Seeker _stranger = new(database);

    private static RunWork Research(Company company) =>
        new() { ResearchCompany = new ResearchCompanyRequest { CompanyId = company.Id } };

    private static RunWork Write(Note note, string instruction = "Explain it") =>
        new()
        {
            Write = new WriteRequest
            {
                NoteId = note.Id,
                Instruction = instruction,
                Title = note.Title,
                Document = "## Stack\n\n⟦cursor⟧",
            },
        };

    private Task<Note> NoteAsync() =>
        _seeker.CreateNoteAsync(new Note { Kind = NoteKind.Pattern, Title = "Monotonic stack", Body = "## Stack" });

    /// <summary>Until the run has saved what it has done, so a test can stop it part-way.</summary>
    private async Task<GetRunReply> ProgressedAsync(Run run)
    {
        while (true)
        {
            var got = await _seeker.GetRunAsync(run.Id);
            if (got.Progress.Steps.Count > 0 || got.Progress.Text.Length > 0)
            {
                return got;
            }

            Assert.True(got.Run.FinishedUnixSeconds == 0, $"It ended first: {got.Run.Status} {got.Run.Error}");

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Research_runs_in_the_background_as_its_owner_and_keeps_its_steps_and_findings()
    {
        var company = await _seeker.CreateCompanyAsync("Halcyon");
        _seeker.Script(new ScriptedResearchModel(
            new { about = "payments api" },
            (ResearchAction.Searching, "halcyon funding"),
            (ResearchAction.Reading, "https://news.example/halcyon")));

        var queued = await _seeker.StartRunAsync(Research(company), "Research Halcyon");

        Assert.Equal(RunStatus.Queued, queued.Status);
        Assert.Equal(RunKind.ResearchCompany, queued.Kind);
        Assert.Equal(company.Id, queued.CompanyId);
        Assert.Equal("Research Halcyon", queued.Label);
        Assert.True(await _seeker.RunNextAsync());
        var done = await _seeker.GetRunAsync(queued.Id);
        Assert.True(done.Run.Status == RunStatus.Succeeded, done.Run.Error);
        Assert.Equal(1, done.Run.Attempt);
        Assert.NotEqual(0, done.Run.FinishedUnixSeconds);
        Assert.Equal(["halcyon funding", "https://news.example/halcyon"], done.Progress.Steps.Select(s => s.Subject));
        Assert.Equal("payments api", done.Result.Company.Company.About);
        Assert.Equal([queued.Id], (await _seeker.ListRunsAsync()).Select(r => r.Id));
        Assert.False(await _seeker.RunNextAsync());
    }

    [Fact]
    public async Task Work_is_checked_as_its_call_would_before_it_is_queued()
    {
        var company = await _seeker.CreateCompanyAsync("Corvid");
        var other = await _seeker.CreateCompanyAsync("Northwind");
        var application = await _seeker.CreateApplicationAsync(Seeker.NewApplication(other.Id));
        var note = await NoteAsync();

        async Task<StatusCode> Refused(Seeker seeker, RunWork work) =>
            (await Assert.ThrowsAsync<RpcException>(() => seeker.StartRunAsync(work))).StatusCode;

        Assert.Equal(StatusCode.NotFound, await Refused(_stranger, Research(company)));
        Assert.Equal(StatusCode.InvalidArgument, await Refused(_seeker, new RunWork
        {
            ReadPosting = new ReadPostingRequest { CompanyId = company.Id, Url = " " },
        }));
        Assert.Equal(StatusCode.NotFound, await Refused(_seeker, new RunWork
        {
            ReadPosting = new ReadPostingRequest
            {
                CompanyId = company.Id, Url = "https://jobs.example/1", ApplicationId = application.Id,
            },
        }));
        Assert.Equal(StatusCode.InvalidArgument, await Refused(_seeker, Write(note, instruction: " ")));
        Assert.Equal(StatusCode.NotFound, await Refused(_stranger, Write(note)));
        var noNote = Write(note);
        noNote.Write.NoteId = "";
        Assert.Equal(StatusCode.InvalidArgument, await Refused(_seeker, noNote));
        Assert.Equal(StatusCode.InvalidArgument, await Refused(_seeker, new RunWork()));
        Assert.Empty(await _seeker.ListRunsAsync());
    }

    [Fact]
    public async Task A_writing_run_keeps_its_text_and_tells_when_its_note_was_saved_since()
    {
        var note = await NoteAsync();
        _seeker.Script(new ScriptedWritingModel("Keep indices ", "on a stack."));

        var run = await _seeker.StartRunAsync(Write(note));
        await _seeker.RunNextAsync();

        var done = await _seeker.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Succeeded, done.Run.Status);
        Assert.Equal(note.Id, done.Run.NoteId);
        Assert.Equal("Keep indices on a stack.", done.Progress.Text);
        Assert.Equal("## Stack\n\n⟦cursor⟧", done.Work.Write.Document);
        Assert.False(done.NoteChanged);
        await _seeker.UpdateNoteAsync(new Note(note) { Body = "## Stack, edited" });
        Assert.True((await _seeker.GetRunAsync(run.Id)).NoteChanged);
    }

    [Fact]
    public async Task A_writing_run_that_fails_part_way_keeps_what_it_wrote_with_the_writers_words()
    {
        var note = await NoteAsync();
        _seeker.Script(new ScriptedWritingModel("Half a ") { Fails = true });

        var run = await _seeker.StartRunAsync(Write(note));
        await _seeker.RunNextAsync();

        var failed = await _seeker.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Failed, failed.Run.Status);
        Assert.Equal(LoopErrors.WriterFailed, failed.Run.Error);
        Assert.Equal("Half a ", failed.Progress.Text);
    }

    [Fact]
    public async Task Starting_the_same_work_again_drops_the_earlier_run_but_not_other_work()
    {
        var company = await _seeker.CreateCompanyAsync("Halcyon");
        var first = await _seeker.StartRunAsync(Research(company));
        var posting = await _seeker.StartRunAsync(new RunWork
        {
            ReadPosting = new ReadPostingRequest { CompanyId = company.Id, Text = "Senior engineer, payments." },
        });

        var second = await _seeker.StartRunAsync(Research(company));

        Assert.Equal([posting.Id, second.Id], (await _seeker.ListRunsAsync()).Select(r => r.Id));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task A_cancelled_run_never_starts_and_a_running_one_stops()
    {
        var company = await _seeker.CreateCompanyAsync("Halcyon");
        var note = await NoteAsync();
        var queued = await _seeker.StartRunAsync(Research(company));
        _seeker.Script(new ScriptedWritingModel("Keep ") { Holds = true });
        var writing = await _seeker.StartRunAsync(Write(note));

        var cancelled = (await _seeker.Runs((s, c) => s.CancelRun(new CancelRunRequest { RunId = queued.Id }, c))).Run;

        Assert.Equal(RunStatus.Cancelled, cancelled.Status);
        var running = _seeker.RunNextAsync();
        await ProgressedAsync(writing);
        await _seeker.Runs((s, c) => s.CancelRun(new CancelRunRequest { RunId = writing.Id }, c));
        await running.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var stopped = await _seeker.GetRunAsync(writing.Id);
        Assert.Equal(RunStatus.Cancelled, stopped.Run.Status);
        Assert.Equal(RunStatus.Cancelled, (await _seeker.GetRunAsync(queued.Id)).Run.Status);
        Assert.False(await _seeker.RunNextAsync());

        await _seeker.Runs((s, c) => s.DismissRuns(new DismissRunsRequest { RunIds = { queued.Id, writing.Id } }, c));
        Assert.Empty(await _seeker.ListRunsAsync());
    }

    [Fact]
    public async Task Research_handed_back_at_shutdown_starts_over_and_a_watch_is_told()
    {
        var company = await _seeker.CreateCompanyAsync("Halcyon");
        _seeker.Script(new ScriptedResearchModel(new { }, (ResearchAction.Searching, "first try")) { Holds = true });
        var run = await _seeker.StartRunAsync(Research(company));
        using var stopping = new CancellationTokenSource();

        var first = _seeker.RunNextAsync(stopping.Token);
        await ProgressedAsync(run);
        await stopping.CancelAsync();
        await first.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.Queued, (await _seeker.GetRunAsync(run.Id)).Run.Status);
        _seeker.Script(new ScriptedResearchModel(new { about = "payments api" }, (ResearchAction.Searching, "second try")));
        await _seeker.RunNextAsync();
        var replies = await _seeker.WatchRunAsync(new WatchRunRequest { RunId = run.Id, Attempt = 1, FromStep = 1 });
        var reply = Assert.Single(replies);
        Assert.True(reply.Restarted);
        Assert.Equal(2, reply.Run.Attempt);
        Assert.Equal(RunStatus.Succeeded, reply.Run.Status);
        Assert.Equal(["second try"], reply.Progress.Steps.Select(s => s.Subject));
        Assert.Equal("payments api", reply.Result.Company.Company.About);
    }

    [Fact]
    public async Task Writing_stopped_by_a_shutdown_ends_with_what_it_wrote_since_a_reply_cant_be_picked_up()
    {
        var note = await NoteAsync();
        _seeker.Script(new ScriptedWritingModel("Keep indices ") { Holds = true });
        var run = await _seeker.StartRunAsync(Write(note));
        using var stopping = new CancellationTokenSource();

        var running = _seeker.RunNextAsync(stopping.Token);
        await ProgressedAsync(run);
        await stopping.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var stopped = await _seeker.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Failed, stopped.Run.Status);
        Assert.Equal(LoopErrors.WriterStopped, stopped.Run.Error);
        Assert.Equal("Keep indices ", stopped.Progress.Text);
    }

    [Fact]
    public async Task A_watch_sends_only_what_the_caller_lacks_then_ends_with_the_run()
    {
        var note = await NoteAsync();
        _seeker.Script(new ScriptedWritingModel("Hello ", "world"));
        var run = await _seeker.StartRunAsync(Write(note));
        await _seeker.RunNextAsync();

        var reply = Assert.Single(await _seeker.WatchRunAsync(new WatchRunRequest { RunId = run.Id, Attempt = 1, FromText = 6 }));

        Assert.False(reply.Restarted);
        Assert.Equal(RunStatus.Succeeded, reply.Run.Status);
        Assert.Equal("world", reply.Progress.Text);
        Assert.Null(reply.Result);
    }

    [Fact]
    public async Task Another_users_runs_are_neither_listed_watched_nor_dismissed()
    {
        var company = await _seeker.CreateCompanyAsync("Halcyon");
        var run = await _seeker.StartRunAsync(Research(company));

        Assert.Empty(await _stranger.ListRunsAsync());
        var error = await Assert.ThrowsAsync<RpcException>(() => _stranger.WatchRunAsync(new WatchRunRequest { RunId = run.Id }));
        Assert.Equal(StatusCode.NotFound, error.StatusCode);
        await _stranger.Runs((s, c) => s.DismissRuns(new DismissRunsRequest { RunIds = { run.Id } }, c));
        var cancel = await Assert.ThrowsAsync<RpcException>(() =>
            _stranger.Runs((s, c) => s.CancelRun(new CancelRunRequest { RunId = run.Id }, c)));
        Assert.Equal(StatusCode.NotFound, cancel.StatusCode);
        Assert.Equal(RunStatus.Queued, Assert.Single(await _seeker.ListRunsAsync()).Status);
        Assert.False(await _stranger.RunNextAsync());
    }
}
