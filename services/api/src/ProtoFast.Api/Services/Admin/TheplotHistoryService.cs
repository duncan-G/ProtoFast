using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Data.ThePlot;
using ProtoFast.DocumentImport.History.Corpus;
using ProtoFast.DocumentImport.History.Runs;
using ProtoFast.Grpc;

namespace ProtoFast.Api.Services.Admin;

/// <summary>
/// Development only (mapped in Program.cs): queues story runs for the worker's history desk and
/// shows what they did. Reads every writer's stories, so it ignores the per-user query filters.
/// </summary>
public sealed class TheplotHistoryService(ThePlotDbContext theplot, HistoryCorpus corpus, StoryRunLedger runs)
    : TheplotHistory.TheplotHistoryBase
{
    private const string App = TheplotAdminService.App;
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private const int MaxWriters = 100;
    private const int DefaultScenes = 6;
    private const int MaxScenes = 20;
    private const int MaxRequestLength = 2000;
    private const int MaxWriterIdLength = 255;
    private const string RunNotFound = "Run not found.";

    public override async Task<GetHistoryCorpusReply> GetCorpus(GetHistoryCorpusRequest request, ServerCallContext context)
    {
        AdminAccess.Require(context, App);
        var stats = await corpus.StatsAsync(context.CancellationToken);
        return new GetHistoryCorpusReply { Books = stats.Books, Passages = stats.Passages };
    }

    public override async Task<ListHistoryWritersReply> ListWriters(ListHistoryWritersRequest request, ServerCallContext context)
    {
        AdminAccess.Require(context, App);
        var writers = await theplot.Stories.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.UserId != OpenLibrary.OwnerId)
            .GroupBy(s => s.UserId)
            .Select(g => new
            {
                Id = g.Key,
                Count = g.Count(),
                Latest = g.OrderByDescending(s => s.DateLastModified).Select(s => s.Title).FirstOrDefault(),
                LastActive = g.Max(s => s.DateLastModified),
            })
            .OrderByDescending(w => w.LastActive)
            .ThenBy(w => w.Id)
            .Take(MaxWriters)
            .ToListAsync(context.CancellationToken);

        var reply = new ListHistoryWritersReply { OpenLibraryId = OpenLibrary.OwnerId };
        reply.Writers.AddRange(writers.Select(w => new HistoryWriter
        {
            Id = w.Id,
            StoryCount = w.Count,
            LatestStoryTitle = w.Latest ?? "",
        }));
        return reply;
    }

    public override async Task<StartHistoryStoryReply> StartStory(StartHistoryStoryRequest request, ServerCallContext context)
    {
        var caller = AdminAccess.Require(context, App);
        var writerId = request.WriterId.Trim();
        if (writerId.Length is 0 or > MaxWriterIdLength)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Pick the writer who will own the story."));
        }

        var asked = request.Request.Trim();
        if (asked.Length > MaxRequestLength)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Keep the request under {MaxRequestLength} characters."));
        }

        var scenes = request.Scenes == 0 ? DefaultScenes : request.Scenes;
        if (scenes is < 1 or > MaxScenes)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Scenes run from 1 to {MaxScenes}."));
        }

        if ((await corpus.StatsAsync(context.CancellationToken)).Books == 0)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition, "The history corpus is empty. Start the history-loader resource in the Aspire dashboard."));
        }

        var id = await runs.CreateAsync(writerId, caller.Subject, asked.Length == 0 ? null : asked, scenes, context.CancellationToken);
        return new StartHistoryStoryReply { RunId = id.ToString() };
    }

    public override async Task<ListHistoryStoryRunsReply> ListStoryRuns(ListHistoryStoryRunsRequest request, ServerCallContext context)
    {
        AdminAccess.Require(context, App);
        var pageSize = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var page = await runs.ListAsync(Math.Max(request.Page, 0), pageSize, context.CancellationToken);
        var reply = new ListHistoryStoryRunsReply { Total = page.Total };
        reply.Runs.AddRange(page.Runs.Select(HistoryAdminMessages.From));
        return reply;
    }

    public override async Task<GetHistoryStoryRunReply> GetStoryRun(GetHistoryStoryRunRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, RunNotFound);
        var run = (Guid.TryParse(request.RunId, out var id) ? await runs.GetAsync(id, context.CancellationToken) : null)
                  ?? throw new RpcException(new Status(StatusCode.NotFound, RunNotFound));
        var reply = new GetHistoryStoryRunReply { Run = HistoryAdminMessages.From(run), BriefJson = run.BriefJson ?? "" };
        reply.Events.AddRange((await runs.EventsAsync(id, context.CancellationToken)).Select(HistoryAdminMessages.From));
        return reply;
    }

    public override async Task<CancelHistoryStoryRunReply> CancelStoryRun(CancelHistoryStoryRunRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, RunNotFound);
        if (!Guid.TryParse(request.RunId, out var id) || await runs.GetAsync(id, context.CancellationToken) is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, RunNotFound));
        }

        return await runs.CancelAsync(id, context.CancellationToken)
            ? new CancelHistoryStoryRunReply()
            : throw new RpcException(new Status(StatusCode.FailedPrecondition, "The run has already ended."));
    }
}
