using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Data.ThePlot;
using ProtoFast.DocumentImport.History.Corpus;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Theplot's console asking for stories from the history corpus, and following the runs.</summary>
public sealed class HistoryAdminTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);
    private readonly Operator _theplot = new(database, "operators", "admin-theplot");

    [Fact]
    public async Task Another_apps_operator_is_refused_and_cannot_probe_run_ids()
    {
        var outsider = new Operator(database, "operators", "admin-protofast", "platform");

        var start = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotHistoryService, StartHistoryStoryReply>(
            (s, c) => s.StartStory(new StartHistoryStoryRequest { WriterId = _writer.UserId }, c)));
        var get = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotHistoryService, GetHistoryStoryRunReply>(
            (s, c) => s.GetStoryRun(new GetHistoryStoryRunRequest { RunId = Guid.NewGuid().ToString() }, c)));

        Assert.Equal(StatusCode.PermissionDenied, start.StatusCode);
        Assert.Equal(StatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task A_run_waits_on_the_corpus_then_queues_for_a_writer_and_can_be_cancelled_once()
    {
        var story = await _writer.CreateStoryAsync();
        var empty = await _theplot.Call<TheplotHistoryService, GetHistoryCorpusReply>((s, c) => s.GetCorpus(new GetHistoryCorpusRequest(), c));
        if (empty.Books == 0)
        {
            var refused = await Assert.ThrowsAsync<RpcException>(() => StartAsync(new StartHistoryStoryRequest { WriterId = _writer.UserId }));
            Assert.Equal(StatusCode.FailedPrecondition, refused.StatusCode);
        }

        await using (var scope = database.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<HistoryCorpus>().AddBookAsync(
                new CorpusBook(Random.Shared.Next(1_000_000, int.MaxValue), "A Diary", "", "", "", "E1", "", null, null),
                ["The regulars came at dawn."], default);
        }

        var badScenes = await Assert.ThrowsAsync<RpcException>(() => StartAsync(new StartHistoryStoryRequest { WriterId = _writer.UserId, Scenes = 21 }));
        var noWriter = await Assert.ThrowsAsync<RpcException>(() => StartAsync(new StartHistoryStoryRequest { WriterId = "  " }));
        Assert.Equal((StatusCode.InvalidArgument, StatusCode.InvalidArgument), (badScenes.StatusCode, noWriter.StatusCode));

        var writers = await _theplot.Call<TheplotHistoryService, ListHistoryWritersReply>((s, c) => s.ListWriters(new ListHistoryWritersRequest(), c));
        Assert.Contains(writers.Writers, w => w.Id == _writer.UserId && w.StoryCount == 1 && w.LatestStoryTitle == story.Title);
        Assert.Equal(OpenLibrary.OwnerId, writers.OpenLibraryId);
        Assert.DoesNotContain(writers.Writers, w => w.Id == OpenLibrary.OwnerId);

        var started = await StartAsync(new StartHistoryStoryRequest { WriterId = _writer.UserId, Request = "  The siege  " });
        var listed = await _theplot.Call<TheplotHistoryService, ListHistoryStoryRunsReply>(
            (s, c) => s.ListStoryRuns(new ListHistoryStoryRunsRequest { PageSize = 100 }, c));
        var run = Assert.Single(listed.Runs, r => r.Id == started.RunId);
        Assert.Equal((_writer.UserId, "The siege", 6, HistoryStoryRunStatus.Queued), (run.WriterId, run.Request, run.Scenes, run.Status));
        Assert.StartsWith("operator-", run.OperatorId);

        await _theplot.Call<TheplotHistoryService, CancelHistoryStoryRunReply>(
            (s, c) => s.CancelStoryRun(new CancelHistoryStoryRunRequest { RunId = started.RunId }, c));
        var again = await Assert.ThrowsAsync<RpcException>(() => _theplot.Call<TheplotHistoryService, CancelHistoryStoryRunReply>(
            (s, c) => s.CancelStoryRun(new CancelHistoryStoryRunRequest { RunId = started.RunId }, c)));
        Assert.Equal(StatusCode.FailedPrecondition, again.StatusCode);

        var detail = await _theplot.Call<TheplotHistoryService, GetHistoryStoryRunReply>(
            (s, c) => s.GetStoryRun(new GetHistoryStoryRunRequest { RunId = started.RunId }, c));
        Assert.Equal((HistoryStoryRunStatus.Cancelled, "", 0), (detail.Run.Status, detail.BriefJson, detail.Events.Count));
    }

    private Task<StartHistoryStoryReply> StartAsync(StartHistoryStoryRequest request) =>
        _theplot.Call<TheplotHistoryService, StartHistoryStoryReply>((s, c) => s.StartStory(request, c));
}
