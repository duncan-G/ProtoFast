using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Data.ThePlot;
using ProtoFast.Grpc;

namespace ProtoFast.Api.Services.Admin;

using GetStoryReply = ProtoFast.Api.Admin.Theplot.GetStoryReply;
using GetStoryRequest = ProtoFast.Api.Admin.Theplot.GetStoryRequest;
using ListStoriesReply = ProtoFast.Api.Admin.Theplot.ListStoriesReply;
using ListStoriesRequest = ProtoFast.Api.Admin.Theplot.ListStoriesRequest;
using StorySummary = ProtoFast.Api.Admin.Theplot.StorySummary;

/// <summary>Every writer's stories, so reads ignore the per-user query filters.</summary>
public sealed class TheplotAdminService(ThePlotDbContext theplot) : TheplotAdmin.TheplotAdminBase
{
    public const string App = "theplot";

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private const string StoryNotFound = "Story not found.";

    public override async Task<ListStoriesReply> ListStories(ListStoriesRequest request, ServerCallContext context)
    {
        AdminAccess.Require(context, App);

        var pageSize = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var page = Math.Max(request.Page, 0);
        var stories = theplot.Stories.IgnoreQueryFilters().AsNoTracking();

        var reply = new ListStoriesReply { Total = await stories.CountAsync(context.CancellationToken) };
        reply.Stories.AddRange(await stories
            .OrderByDescending(s => s.DateLastModified)
            .ThenBy(s => s.Id)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(s => new StorySummary
            {
                Id = s.Id.ToString(),
                Title = s.Title,
                OwnerId = s.UserId,
                SceneCount = s.Containers.SelectMany(c => c.Scenes).Count(),
                CreatedUnixSeconds = new DateTimeOffset(s.DateCreated).ToUnixTimeSeconds(),
                LastModifiedUnixSeconds = new DateTimeOffset(s.DateLastModified).ToUnixTimeSeconds(),
            })
            .ToListAsync(context.CancellationToken));
        return reply;
    }

    public override async Task<GetStoryReply> GetStory(GetStoryRequest request, ServerCallContext context)
    {
        // Every row this service reads is theplot's.
        AdminAccess.RequireRow(context, App, StoryNotFound);

        if (!Guid.TryParse(request.StoryId, out var storyId))
        {
            throw new RpcException(new Status(StatusCode.NotFound, StoryNotFound));
        }

        var reply = await theplot.Stories.IgnoreQueryFilters().AsNoTracking()
                        .Where(s => s.Id == storyId)
                        .Select(s => new GetStoryReply
                        {
                            Story = new StorySummary
                            {
                                Id = s.Id.ToString(),
                                Title = s.Title,
                                OwnerId = s.UserId,
                                SceneCount = s.Containers.SelectMany(c => c.Scenes).Count(),
                                CreatedUnixSeconds = new DateTimeOffset(s.DateCreated).ToUnixTimeSeconds(),
                                LastModifiedUnixSeconds = new DateTimeOffset(s.DateLastModified).ToUnixTimeSeconds(),
                            },
                            CharacterCount = s.Characters.Count,
                            LocationCount = s.Locations.Count,
                            PropCount = s.Props.Count,
                        })
                        .FirstOrDefaultAsync(context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.NotFound, StoryNotFound));
        return reply;
    }
}
