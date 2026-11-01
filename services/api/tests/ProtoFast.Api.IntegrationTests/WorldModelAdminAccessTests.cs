using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using Xunit;
using WorldModelBuildStepRecord = ProtoFast.Data.ThePlot.Entities.WorldModelBuildStep;
using WorldModelStepKindRecord = ProtoFast.Data.ThePlot.Entities.WorldModelStepKind;
using WorldModelStepStatusRecord = ProtoFast.Data.ThePlot.Entities.WorldModelStepStatus;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Who may read world model builds through theplot's console, and what they see.</summary>
public sealed class WorldModelAdminAccessTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);
    private readonly Operator _theplot = new(database, "operators", "admin-theplot");

    [Fact]
    public async Task Another_apps_operator_is_refused_and_cannot_probe_build_ids()
    {
        var outsider = new Operator(database, "operators", "admin-protofast", "platform");
        var build = await StartAsync();

        var list = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotWorldModelsService, ListWorldModelBuildsReply>(
            (s, c) => s.ListBuilds(new ListWorldModelBuildsRequest(), c)));
        var get = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotWorldModelsService, GetWorldModelBuildReply>(
            (s, c) => s.GetBuild(new GetWorldModelBuildRequest { BuildId = build.Id }, c)));
        var start = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotWorldModelsService, StartWorldModelBuildReply>(
            (s, c) => s.StartBuild(new StartWorldModelBuildRequest { StoryId = build.StoryId }, c)));
        var cancel = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotWorldModelsService, CancelWorldModelBuildReply>(
            (s, c) => s.CancelBuild(new CancelWorldModelBuildRequest { BuildId = build.Id }, c)));

        Assert.Equal(StatusCode.PermissionDenied, list.StatusCode);
        Assert.Equal(StatusCode.NotFound, get.StatusCode);
        Assert.Equal(StatusCode.NotFound, start.StatusCode);
        Assert.Equal(StatusCode.NotFound, cancel.StatusCode);
    }

    [Fact]
    public async Task Theplots_operator_sees_every_writers_builds_with_their_steps_and_costs()
    {
        var build = await StartAsync();
        var buildId = Guid.Parse(build.Id);
        await using (var scope = database.Services.CreateAsyncScope())
        {
            using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(_writer.UserId);
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateReadWrite("worker");
            var steps = scope.ServiceProvider.GetRequiredService<IWorldModelBuildStepRepository>();
            await steps.AddAsync(new WorldModelBuildStepRecord
            {
                Id = Guid.NewGuid(), BuildId = buildId, Sequence = 1, Kind = WorldModelStepKindRecord.Seed, Title = "Seed",
                Status = WorldModelStepStatusRecord.Finished, StartedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow, Summary = "2 locations",
            }, default);
            await steps.AddAsync(new WorldModelBuildStepRecord
            {
                Id = Guid.NewGuid(), BuildId = buildId, Sequence = 2, Kind = WorldModelStepKindRecord.ReadScene, Title = "Scene 1",
                Status = WorldModelStepStatusRecord.Finished, StartedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow,
                ModelId = "claude-sonnet-5-5", InputTokens = 1200, OutputTokens = 300, Cost = 0.0125m,
                Prompt = "<scene/>", Reply = "{}", Summary = "1 new part",
            }, default);
            await unitOfWork.CommitAsync(default);
        }

        var listed = await _theplot.Call<TheplotWorldModelsService, ListWorldModelBuildsReply>(
            (s, c) => s.ListBuilds(new ListWorldModelBuildsRequest { Status = WorldModelBuildStatus.Queued, PageSize = 100 }, c));
        var detail = await _theplot.Call<TheplotWorldModelsService, GetWorldModelBuildReply>(
            (s, c) => s.GetBuild(new GetWorldModelBuildRequest { BuildId = build.Id }, c));

        var header = Assert.Single(listed.Builds, b => b.Id == build.Id);
        Assert.Equal(("The Signal in the Scrap", _writer.UserId, WorldModelBuildStatus.Queued, 2), (header.StoryTitle, header.OwnerId, header.Status, header.Steps));
        Assert.Equal(2, detail.Steps.Count);
        var read = detail.Steps[1];
        Assert.Equal((WorldModelStepKind.ReadScene, "claude-sonnet-5-5", 1200L, 12_500L, "<scene/>", "1 new part"),
            (read.Kind, read.ModelId, read.InputTokens, read.CostUsdMicros, read.Prompt, read.Summary));
    }

    private async Task<WorldModelBuildHeader> StartAsync()
    {
        var story = await _writer.CreateStoryAsync();
        return (await _theplot.Call<TheplotWorldModelsService, StartWorldModelBuildReply>(
            (s, c) => s.StartBuild(new StartWorldModelBuildRequest { StoryId = story.Id }, c))).Build;
    }
}
