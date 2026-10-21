using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Imaging;
using ProtoFast.Api.Services.Speech;
using ProtoFast.Api.Services.WorldModels;
using ProtoFast.Data.ThePlot;
using ProtoFast.Grpc;
using WorldModelBuildStatusRecord = ProtoFast.Data.ThePlot.Entities.WorldModelBuildStatus;

namespace ProtoFast.Api.Services.Admin;

using SceneElementType = ProtoFast.Data.ThePlot.Entities.SceneElementType;

/// <summary>Every writer's world model builds, so reads ignore the per-user query filters.</summary>
public sealed class TheplotWorldModelsService(
    ThePlotDbContext theplot,
    WorldModelBuilds builds,
    ViewPictures views,
    ISpeechSynthesizer voices) : TheplotWorldModels.TheplotWorldModelsBase
{
    public const string App = TheplotAdminService.App;

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private const string BuildNotFound = "Build not found.";
    private const string StoryNotFound = "Story not found.";
    private const string BeatNotFound = "Beat not found.";
    private const string ViewNotFound = "View not found.";
    private const string PictureNotFound = "Picture not found.";

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

        if (request.StoryId.Length > 0)
        {
            var storyId = Guid.TryParse(request.StoryId, out var parsed) ? parsed : Guid.Empty;
            builds = builds.Where(b => b.StoryId == storyId);
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

    public override async Task<StartWorldModelBuildReply> StartBuild(StartWorldModelBuildRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, StoryNotFound);

        if (!Guid.TryParse(request.StoryId, out var storyId))
        {
            throw new RpcException(new Status(StatusCode.NotFound, StoryNotFound));
        }

        var writerId = await theplot.Stories.IgnoreQueryFilters().AsNoTracking()
                           .Where(s => s.Id == storyId)
                           .Select(s => s.UserId)
                           .FirstOrDefaultAsync(context.CancellationToken)
                       ?? throw new RpcException(new Status(StatusCode.NotFound, StoryNotFound));
        var buildId = await builds.StartAsync(writerId, storyId, context.CancellationToken);
        return new StartWorldModelBuildReply { Build = await HeaderAsync(buildId, context.CancellationToken) };
    }

    public override async Task<CancelWorldModelBuildReply> CancelBuild(CancelWorldModelBuildRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, BuildNotFound);

        if (!Guid.TryParse(request.BuildId, out var buildId))
        {
            throw new RpcException(new Status(StatusCode.NotFound, BuildNotFound));
        }

        var writerId = await theplot.WorldModelBuilds.IgnoreQueryFilters().AsNoTracking()
                           .Where(b => b.Id == buildId)
                           .Select(b => b.UserId)
                           .FirstOrDefaultAsync(context.CancellationToken)
                       ?? throw new RpcException(new Status(StatusCode.NotFound, BuildNotFound));
        await builds.CancelAsync(writerId, buildId, context.CancellationToken);
        return new CancelWorldModelBuildReply { Build = await HeaderAsync(buildId, context.CancellationToken) };
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

    public override async Task<PictureViewReply> PictureView(PictureViewRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, StoryNotFound);

        if (!Guid.TryParse(request.StoryId, out var storyId))
        {
            throw new RpcException(new Status(StatusCode.NotFound, StoryNotFound));
        }

        var world = await StoryWorld.ReadAsync(theplot, storyId, context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.NotFound, StoryNotFound));
        var view = SceneViews.Find(world, request.SceneId, request.Camera, request.Toward, request.State)
                   ?? throw new RpcException(new Status(StatusCode.NotFound, ViewNotFound));
        try
        {
            var (picture, image) = await views.PictureAsync(world, view, context.CancellationToken);
            return new PictureViewReply { Picture = WorldModelAdminMessages.Picture(picture), Image = ByteString.CopyFrom(image) };
        }
        catch (ImageException e)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message));
        }
    }

    public override async Task<GetViewPictureReply> GetViewPicture(GetViewPictureRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, PictureNotFound);

        if (!Guid.TryParse(request.StoryId, out var storyId) || !Guid.TryParse(request.PictureId, out var pictureId))
        {
            throw new RpcException(new Status(StatusCode.NotFound, PictureNotFound));
        }

        var ownerId = await theplot.Stories.IgnoreQueryFilters().AsNoTracking()
                          .Where(s => s.Id == storyId)
                          .Select(s => s.UserId)
                          .FirstOrDefaultAsync(context.CancellationToken)
                      ?? throw new RpcException(new Status(StatusCode.NotFound, PictureNotFound));
        var (image, contentType) = await views.ReadAsync(ownerId, storyId, pictureId, context.CancellationToken)
                                   ?? throw new RpcException(new Status(StatusCode.NotFound, PictureNotFound));
        return new GetViewPictureReply { Image = ByteString.CopyFrom(image), ContentType = contentType };
    }

    public override async Task<SpeakBeatReply> SpeakBeat(SpeakBeatRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, StoryNotFound);

        if (!Guid.TryParse(request.StoryId, out var storyId) || !Guid.TryParse(request.BeatId, out var beatId))
        {
            throw new RpcException(new Status(StatusCode.NotFound, BeatNotFound));
        }

        var beat = await theplot.SceneElements.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Id == beatId && e.Scene.Container.StoryId == storyId)
            .Select(e => new { e.Type, e.Text, e.SpeakerId })
            .FirstOrDefaultAsync(context.CancellationToken);
        var text = BeatText.Plain(beat?.Text ?? "");
        if (beat is null || text.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.NotFound, BeatNotFound));
        }

        try
        {
            var voice = await voices.VoiceForAsync(beat.Type == SceneElementType.Dialogue ? beat.SpeakerId : null, context.CancellationToken);
            var line = await voices.SpeakAsync(text, voice, context.CancellationToken);
            return new SpeakBeatReply
            {
                Audio = ByteString.CopyFrom(line.Audio),
                ContentType = line.ContentType,
                VoiceName = voice.Name,
                CostUsdMicros = line.CostUsdMicros,
            };
        }
        catch (SpeechException e)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message));
        }
    }

    private async Task<WorldModelBuildHeader> HeaderAsync(Guid buildId, CancellationToken cancellationToken) =>
        await theplot.WorldModelBuilds.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.Id == buildId)
            .Select(WorldModelAdminMessages.Header)
            .FirstAsync(cancellationToken);
}
