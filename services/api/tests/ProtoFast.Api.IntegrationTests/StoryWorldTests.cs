using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>A story's world as theplot's console reads it: facts folded over moments of story time.</summary>
public sealed class StoryWorldTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);
    private readonly Operator _theplot = new(database, "operators", "admin-theplot");

    [Fact]
    public async Task Facts_hold_over_moments_with_the_beats_that_said_them_and_the_ones_that_lost()
    {
        var story = await _writer.CreateStoryAsync();
        var wing = await _writer.CreateLocationAsync(story.Id, "TRAUMA WING");
        var first = await _writer.WriteFirstSceneAsync(story,
            Heading(wing.Id, "NIGHT"), Action("A gurney blocks the hallway."), Action("A light flickers."));
        var second = await _writer.CreateSceneAsync(story.Containers[0].Id, 1, "Later",
            Heading(wing.Id, "DAY"), Action("An orderly wheels the gurney away."), Action("The light is steady now."));
        second = await _writer.GetSceneAsync(second.Id);
        var (s1b1, s1b2) = (Guid.Parse(first.Elements[1].Id), Guid.Parse(first.Elements[2].Id));
        var (s2b1, s2b2) = (Guid.Parse(second.Elements[1].Id), Guid.Parse(second.Elements[2].Id));

        var wingId = Guid.Parse(wing.Id);
        var middle = new LocationPart { Id = Guid.NewGuid(), LocationId = wingId, Name = "hallway: middle", Position = 1 };
        var gurney = new LocationObject { Id = Guid.NewGuid(), UserId = _writer.UserId, StoryId = Guid.Parse(story.Id), Name = "gurney", Kind = ObjectKind.Prop };
        var light = new LocationObject { Id = Guid.NewGuid(), UserId = _writer.UserId, StoryId = Guid.Parse(story.Id), Name = "light", Kind = ObjectKind.Fixture };
        await using (var scope = database.Services.CreateAsyncScope())
        {
            using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(_writer.UserId);
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateReadWrite("worker");
            await scope.ServiceProvider.GetRequiredService<ILocationPartRepository>().AddAsync(middle, default);
            var objects = scope.ServiceProvider.GetRequiredService<ILocationObjectRepository>();
            await objects.AddAsync(gurney, default);
            await objects.AddAsync(light, default);
            var observations = scope.ServiceProvider.GetRequiredService<ILocationObservationRepository>();
            foreach (var observation in new[]
                     {
                         At(s1b1, gurney.Id, wingId, middle.Id, ObservationBasis.Stated),
                         At(s1b2, light.Id, wingId, null, ObservationBasis.Stated),
                         State(s1b2, light.Id, "lit", "flickering"),
                         At(s2b1, gurney.Id, null, null, ObservationBasis.Stated),
                         At(s2b1, light.Id, wingId, middle.Id, ObservationBasis.Implied),
                         At(s2b2, light.Id, wingId, null, ObservationBasis.Stated),
                         State(s2b2, light.Id, "lit", "steady"),
                     })
            {
                await observations.AddAsync(observation, default);
            }

            await unitOfWork.CommitAsync(default);
        }

        var world = await _theplot.Call<TheplotWorldModelsService, GetWorldReply>(
            (s, c) => s.GetWorld(new GetWorldRequest { StoryId = story.Id }, c));

        // Moments: 0 opens scene 1, 1–2 are its beats, 3 opens scene 2, 4–5 are its beats.
        Assert.Equal([2, 2], world.Scenes.Select(s => s.Beats.Count));
        Assert.Equal(wing.Id, world.Scenes[1].LocationId);
        var gurneyAt = Assert.Single(world.Facts, f => f.Edge == WorldEdge.At && f.ObjectId == gurney.Id.ToString());
        Assert.Equal((1, 4, middle.Id.ToString()), (gurneyAt.Start, gurneyAt.End, gurneyAt.Place.PartId));
        var lightAt = Assert.Single(world.Facts, f => f.Edge == WorldEdge.At && f.ObjectId == light.Id.ToString());
        Assert.Equal((0, 6, 2), (lightAt.Start, lightAt.End, lightAt.Said));
        Assert.Equal([5], lightAt.Confirmed);
        Assert.Equal([(2, 5, "flickering"), (5, 6, "steady")],
            world.Facts.Where(f => f.Edge == WorldEdge.State).OrderBy(f => f.Start).Select(f => (f.Start, f.End, f.Value)));
        var lost = Assert.Single(world.Conflicts);
        Assert.Equal((world.Facts.IndexOf(lightAt), 4, WorldBasis.Implied), (lost.Fact, lost.Moment, lost.Basis));
        Assert.Equal(WorldObjectKind.Fixture, Assert.Single(world.Objects, o => o.Name == "light").Kind);
    }

    [Fact]
    public async Task Another_apps_operator_cannot_read_a_world()
    {
        var outsider = new Operator(database, "operators", "admin-protofast", "platform");
        var story = await _writer.CreateStoryAsync();

        var error = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotWorldModelsService, GetWorldReply>(
            (s, c) => s.GetWorld(new GetWorldRequest { StoryId = story.Id }, c)));

        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    private static LocationObservation At(Guid beat, Guid objectId, Guid? location, Guid? part, ObservationBasis basis) => new()
    {
        Id = Guid.NewGuid(), SceneElementId = beat, Edge = ObservationEdge.At, ObjectId = objectId,
        PlaceLocationId = location, PlacePartId = part, Basis = basis, Origin = ObservationOrigin.Extracted,
    };

    private static LocationObservation State(Guid beat, Guid objectId, string property, string value) => new()
    {
        Id = Guid.NewGuid(), SceneElementId = beat, Edge = ObservationEdge.State, ObjectId = objectId,
        Property = property, Value = value, Basis = ObservationBasis.Stated, Origin = ObservationOrigin.Extracted,
    };
}
