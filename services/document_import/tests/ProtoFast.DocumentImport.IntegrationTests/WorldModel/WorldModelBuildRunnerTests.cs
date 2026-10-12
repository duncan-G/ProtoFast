using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoFast.Data.ThePlot;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using ProtoFast.DocumentImport.Screenplay.Models;
using ProtoFast.DocumentImport.Worker.WorldModel;
using ProtoFast.Storage.Abstractions;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.WorldModel;

/// <summary>
/// A whole build against the real tables, with the model scripted: the doc's trauma-wing example
/// read scene by scene, in two locations side by side, and what the ledger says afterwards.
/// </summary>
public sealed class WorldModelBuildRunnerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly string _userId = $"writer-{Guid.NewGuid():N}";
    private readonly SceneScriptedModels _models = new();
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(postgres.DataSource);
        services.AddThePlotData();
        services.AddSingleton<ILanguageModelFactory>(_models);
        services.AddKeyedSingleton<IMessageQueue>(WorldModelQueues.BuildQueueKey, new NoQueue());
        services.AddWorldModelBuilding(options => options.LocationConcurrency = 2);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ThePlotDbContext>().Database.MigrateAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task A_build_reads_every_scene_into_the_graph_and_records_each_step_with_its_cost()
    {
        var story = await SeedStoryAsync();
        var (lot, wing, bryan) = (story.Locations["PARKING LOT"], story.Locations["TRAUMA WING"], story.Characters["BRYAN"]);
        var (s1, s2, s3) = (story.Scenes[0], story.Scenes[1], story.Scenes[2]);

        _models.Script(s1.Id, _ => $$"""
            {"parts": [], "objects": [], "aliases": [],
             "edges": [{"beatId": "{{s1.Beats[0]}}", "edge": "character_at", "characterId": "{{bryan}}", "locationId": "{{lot}}", "basis": "stated"}]}
            """);
        _models.Script(s2.Id, _ => $$"""
            {"parts": [{"ref": "p1", "name": "hallway: start", "position": 0}, {"ref": "p2", "name": "hallway: end"}],
             "objects": [{"ref": "o1", "name": "double doors", "aliases": ["the doors"], "kind": "Fixture"},
                         {"ref": "o2", "name": "gurney", "kind": "Prop"},
                         {"ref": "o3", "name": "light", "kind": "Fixture"}],
             "aliases": [],
             "edges": [
               {"beatId": "{{s2.Beats[0]}}", "edge": "at", "objectId": "o1", "partId": "p1", "basis": "stated"},
               {"beatId": "{{s2.Beats[0]}}", "edge": "character_at", "characterId": "{{bryan}}", "partId": "p1", "basis": "stated"},
               {"beatId": "{{s2.Beats[0]}}", "edge": "beat_at", "partId": "p1", "basis": "implied"},
               {"beatId": "{{s2.Beats[1]}}", "edge": "at", "objectId": "o2", "partId": "p2", "basis": "stated"},
               {"beatId": "{{s2.Beats[1]}}", "edge": "at", "objectId": "o3", "partId": "p2", "basis": "stated"},
               {"beatId": "{{s2.Beats[1]}}", "edge": "state", "objectId": "o3", "property": "lit", "value": "flickering", "basis": "stated"},
               {"beatId": "{{s2.Beats[1]}}", "edge": "next", "partId": "p1", "nextPartId": "p2"},
               {"beatId": "{{s2.Beats[1]}}", "edge": "at", "objectId": "nobody", "partId": "p2", "basis": "stated"}
             ]}
            """);
        _models.Script(s3.Id, prompt => prompt.Contains("name=\"gurney\"")
            ? $$"""
              {"parts": [], "objects": [{"ref": "o1", "name": "the stretcher", "aliases": ["gurney"], "kind": "Prop"}],
               "aliases": [{"objectId": "o1", "alias": "the trolley"}],
               "edges": [{"beatId": "{{s3.Beats[0]}}", "edge": "at", "objectId": "o1", "basis": "stated"}]}
              """
            : throw new InvalidOperationException("The third scene did not see the gurney the second one placed."));

        await using var scope = _services.CreateAsyncScope();
        var build = await StartBuildAsync(story.Id);
        var built = await scope.ServiceProvider.GetRequiredService<WorldModelBuildRunner>()
            .RunAsync(new WorldModelBuildRequested(build, story.Id, _userId), Ct);

        Assert.True(built);
        await using var check = _services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<ThePlotDbContext>();
        var ledger = await db.WorldModelBuilds.IgnoreQueryFilters().Include(b => b.Steps.OrderBy(s => s.Sequence)).SingleAsync(b => b.Id == build, Ct);
        Assert.Equal((WorldModelBuildStatus.Finished, 3, 3, 0.03m, 1), (ledger.Status, ledger.ScenesTotal, ledger.ScenesDone, ledger.Cost, ledger.Deliveries));
        Assert.Equal(
            [WorldModelStepKind.Seed, WorldModelStepKind.ReadScene, WorldModelStepKind.ReadScene, WorldModelStepKind.ReadScene, WorldModelStepKind.Finish],
            ledger.Steps.OrderBy(s => s.Sequence).Select(s => s.Kind));
        Assert.All(ledger.Steps, s => Assert.Equal(WorldModelStepStatus.Finished, s.Status));
        var second = ledger.Steps.Single(s => s.SceneId == s2.Id);
        Assert.Equal(("scripted-medium", 1000L, 0.01m), (second.ModelId, second.InputTokens, second.Cost));
        Assert.Contains("2 new parts, 3 new objects", second.Summary);
        Assert.Contains("unknown object nobody", second.Summary);
        Assert.Contains("<beat id=\"" + s2.Beats[0] + "\"", second.Prompt);

        var parts = await db.LocationParts.IgnoreQueryFilters().Where(p => p.LocationId == wing).OrderBy(p => p.Position).ToListAsync(Ct);
        Assert.Equal([("hallway: start", 0), ("hallway: end", 1)], parts.Select(p => (p.Name, p.Position!.Value)));
        var objects = await db.LocationObjects.IgnoreQueryFilters().Where(o => o.StoryId == story.Id).OrderBy(o => o.Name).ToListAsync(Ct);
        Assert.Equal(["double doors", "gurney", "light"], objects.Select(o => o.Name));
        var gurney = objects.Single(o => o.Name == "gurney");
        Assert.Equal(["the stretcher", "the trolley"], gurney.Aliases.Order());

        var observations = await db.LocationObservations.IgnoreQueryFilters()
            .Where(o => o.SceneElement.Scene.Container.StoryId == story.Id).ToListAsync(Ct);
        Assert.Equal(8, observations.Count(o => o.Origin == ObservationOrigin.Extracted));
        var ruled = Assert.Single(observations, o => o.Origin == ObservationOrigin.Rule);
        Assert.Equal((ObservationEdge.Connects, lot, wing, parts[0].Id), (ruled.Edge, ruled.PlaceLocationId, ruled.OtherLocationId, ruled.OtherPartId));
        Assert.Equal((2, 3, 9), (ledger.PartCount, ledger.ObjectCount, ledger.ObservationCount));
        Assert.Contains("1 connection between scenes", ledger.Steps.Last().Summary);

        // Each scene was read once, and the wing's scenes in story order.
        Assert.Equal(3, _models.Calls.Count);
        Assert.True(_models.Calls.FindIndex(c => c.SceneId == s2.Id) < _models.Calls.FindIndex(c => c.SceneId == s3.Id));
    }

    [Fact]
    public async Task A_build_delivered_again_carries_on_from_the_scenes_not_yet_read()
    {
        var story = await SeedStoryAsync();
        var (s1, s2, s3) = (story.Scenes[0], story.Scenes[1], story.Scenes[2]);
        var empty = """{"parts": [], "objects": [], "aliases": [], "edges": []}""";
        _models.Script(s1.Id, _ => empty);
        _models.Script(s2.Id, _ => throw new InvalidOperationException("The model is down."));
        _models.Script(s3.Id, _ => empty);
        var build = await StartBuildAsync(story.Id);
        var request = new WorldModelBuildRequested(build, story.Id, _userId);

        await using (var first = _services.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => first.ServiceProvider.GetRequiredService<WorldModelBuildRunner>().RunAsync(request, Ct));
        }

        // The failing scene stops its location's group; the other location's scene may or may not have landed by then.
        HashSet<Guid> read;
        await using (var after = _services.CreateAsyncScope())
        {
            read = (await after.ServiceProvider.GetRequiredService<ThePlotDbContext>().WorldModelBuildSteps.IgnoreQueryFilters()
                .Where(s => s.BuildId == build && s.Kind == WorldModelStepKind.ReadScene && s.Status == WorldModelStepStatus.Finished)
                .Select(s => s.SceneId!.Value)
                .ToListAsync(Ct)).ToHashSet();
        }

        Assert.DoesNotContain(s2.Id, read);
        Assert.DoesNotContain(s3.Id, read);

        await _services.GetRequiredService<WorldModelLedger>().FailAsync(build, _userId, "The model is down.", final: false, Ct);
        _models.Script(s2.Id, _ => empty);
        _models.Calls.Clear();
        await using (var second = _services.CreateAsyncScope())
        {
            Assert.True(await second.ServiceProvider.GetRequiredService<WorldModelBuildRunner>().RunAsync(request, Ct));
        }

        await using var check = _services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<ThePlotDbContext>();
        var ledger = await db.WorldModelBuilds.IgnoreQueryFilters().Include(b => b.Steps).SingleAsync(b => b.Id == build, Ct);
        Assert.Equal((WorldModelBuildStatus.Finished, 2, 3), (ledger.Status, ledger.Deliveries, ledger.ScenesDone));
        Assert.Equal(story.Scenes.Select(s => s.Id).Where(id => !read.Contains(id)).Order(), _models.Calls.Select(c => c.SceneId).Order());
        Assert.Contains(ledger.Steps, s => s.Status == WorldModelStepStatus.Failed && s.SceneId == s2.Id);
        Assert.Equal(3, ledger.Steps.Count(s => s.Kind == WorldModelStepKind.ReadScene && s.Status == WorldModelStepStatus.Finished));
    }

    [Fact]
    public async Task A_build_its_owner_cancelled_is_not_claimed()
    {
        var story = await SeedStoryAsync();
        var build = await StartBuildAsync(story.Id, WorldModelBuildStatus.Cancelled);

        await using var scope = _services.CreateAsyncScope();
        var built = await scope.ServiceProvider.GetRequiredService<WorldModelBuildRunner>()
            .RunAsync(new WorldModelBuildRequested(build, story.Id, _userId), Ct);

        Assert.False(built);
        Assert.Empty(_models.Calls);
    }

    private sealed record SeededStory(Guid Id, Dictionary<string, Guid> Locations, Dictionary<string, Guid> Characters, List<SeededScene> Scenes);

    private sealed record SeededScene(Guid Id, List<Guid> Beats);

    private async Task<SeededStory> SeedStoryAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(_userId);
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateReadWrite("seed");
        var lot = new Location { Id = Guid.NewGuid(), Name = "PARKING LOT", Setting = LocationSetting.Exterior, Hue = 200 };
        var wing = new Location { Id = Guid.NewGuid(), Name = "TRAUMA WING", Setting = LocationSetting.Interior, Hue = 20 };
        var bryan = new Character { Id = Guid.NewGuid(), Name = "BRYAN", Kind = "Human", Hue = 10 };
        var scenes = new List<SeededScene>();
        var container = new Container { Id = Guid.NewGuid(), Label = "Act I", Position = 0 };
        foreach (var (title, location, beats) in new[]
                 {
                     ("Arrival", lot, new[] { "Bryan slams the car door and runs." }),
                     ("The wing", wing, new[] { "Bryan shoves through the double doors.", "A gurney blocks the hallway; at the far end a light flickers." }),
                     ("Morning", wing, new[] { "An orderly wheels the gurney away." }),
                 })
        {
            var scene = new Scene { Id = Guid.NewGuid(), Title = title, Position = scenes.Count };
            scene.Elements.Add(new SceneElement { Id = Guid.NewGuid(), Position = 0, Type = SceneElementType.Heading, Location = location, TimeOfDay = "NIGHT" });
            var beatIds = new List<Guid>();
            foreach (var text in beats)
            {
                var beat = new SceneElement { Id = Guid.NewGuid(), Position = scene.Elements.Count, Type = SceneElementType.Action, Text = text };
                scene.Elements.Add(beat);
                beatIds.Add(beat.Id);
            }

            container.Scenes.Add(scene);
            scenes.Add(new SeededScene(scene.Id, beatIds));
        }

        var story = new Story { Id = Guid.NewGuid(), Title = "Trauma", Containers = [container], Characters = [bryan], Locations = [lot, wing] };
        await scope.ServiceProvider.GetRequiredService<IStoryRepository>().AddAsync(story, Ct);
        await unitOfWork.CommitAsync(Ct);
        return new SeededStory(
            story.Id,
            new Dictionary<string, Guid> { [lot.Name] = lot.Id, [wing.Name] = wing.Id },
            new Dictionary<string, Guid> { [bryan.Name] = bryan.Id },
            scenes);
    }

    private async Task<Guid> StartBuildAsync(Guid storyId, WorldModelBuildStatus status = WorldModelBuildStatus.Queued)
    {
        await using var scope = _services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(_userId);
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateReadWrite("start");
        var build = new WorldModelBuild { Id = Guid.NewGuid(), StoryId = storyId, Status = status, ScenesTotal = 3 };
        await scope.ServiceProvider.GetRequiredService<IWorldModelBuildRepository>().AddAsync(build, Ct);
        await unitOfWork.CommitAsync(Ct);
        return build.Id;
    }

    private sealed class NoQueue : IMessageQueue
    {
        public string Name => "none";

        public Task SendAsync<T>(T message, string groupId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(int maxMessages, CancellationToken ct = default) => throw new NotSupportedException();

        public Task DeleteAsync(string receiptHandle, CancellationToken ct = default) => throw new NotSupportedException();

        public Task ExtendVisibilityAsync(string receiptHandle, TimeSpan timeout, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
