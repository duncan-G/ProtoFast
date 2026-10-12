using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Data.ThePlot;
using ProtoFast.Grpc;
using WorldModelBuildStatusRecord = ProtoFast.Data.ThePlot.Entities.WorldModelBuildStatus;

namespace ProtoFast.Api.Services.Admin;

/// <summary>Every writer's world model builds, so reads ignore the per-user query filters.</summary>
public sealed class TheplotWorldModelsService(ThePlotDbContext theplot) : TheplotWorldModels.TheplotWorldModelsBase
{
    public const string App = TheplotAdminService.App;

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private const string BuildNotFound = "Build not found.";
    private const string StoryNotFound = "Story not found.";

    public override async Task<ListWorldModelBuildsReply> ListBuilds(ListWorldModelBuildsRequest request, ServerCallContext context)
    {
        AdminAccess.Require(context, App);

        var pageSize = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var page = Math.Max(request.Page, 0);
        var builds = theplot.WorldModelBuilds.IgnoreQueryFilters().AsNoTracking();
        if (WorldModelAdminMessages.ToStatus(request.Status) is { } status)
        {
            builds = builds.Where(b => b.Status == status);
        }

        var reply = new ListWorldModelBuildsReply { Total = await builds.CountAsync(context.CancellationToken) };
        reply.Builds.AddRange(await builds
            .OrderByDescending(b => b.DateCreated)
            .ThenByDescending(b => b.Id)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(WorldModelAdminMessages.Header)
            .ToListAsync(context.CancellationToken));
        return reply;
    }

    public override async Task<GetWorldModelBuildReply> GetBuild(GetWorldModelBuildRequest request, ServerCallContext context)
    {
        // Every row this service reads is theplot's.
        AdminAccess.RequireRow(context, App, BuildNotFound);

        if (!Guid.TryParse(request.BuildId, out var buildId))
        {
            throw new RpcException(new Status(StatusCode.NotFound, BuildNotFound));
        }

        var build = await theplot.WorldModelBuilds.IgnoreQueryFilters().AsNoTracking()
                        .Where(b => b.Id == buildId)
                        .Select(WorldModelAdminMessages.Header)
                        .FirstOrDefaultAsync(context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.NotFound, BuildNotFound));
        var steps = await theplot.WorldModelBuildSteps.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.BuildId == buildId)
            .OrderBy(s => s.Sequence)
            .ThenBy(s => s.Id)
            .ToListAsync(context.CancellationToken);

        var reply = new GetWorldModelBuildReply { Build = build };
        reply.Steps.AddRange(steps.Select(WorldModelAdminMessages.From));
        return reply;
    }

    public override async Task<GetWorldReply> GetWorld(GetWorldRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, StoryNotFound);

        if (!Guid.TryParse(request.StoryId, out var storyId))
        {
            throw new RpcException(new Status(StatusCode.NotFound, StoryNotFound));
        }

        return await StoryWorld.ReadAsync(theplot, storyId, context.CancellationToken)
               ?? throw new RpcException(new Status(StatusCode.NotFound, StoryNotFound));
    }
}
