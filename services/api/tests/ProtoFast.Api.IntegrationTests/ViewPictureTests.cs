using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Api.Services.Imaging;
using ProtoFast.Data.ThePlot;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>theplot's console pictures each place a scene's camera films from, empty, and keeps the pictures.</summary>
public sealed class ViewPictureTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);
    private readonly Operator _theplot = new(database, "operators", "admin-theplot");

    [Fact]
    public async Task A_scene_has_one_view_for_each_place_its_camera_films_from()
    {
        var diner = await DinerAsync();
        var parts = await ReadAsync(diner);

        var world = await WorldAsync(diner);

        Assert.Equal(new[]
        {
            (0, Place(diner.Location, parts.Counter), Place(diner.Location, parts.Entrance), "1,3"),
            (0, Place(diner.Location, parts.Booths), Place(diner.Location, parts.Counter), "2,4"),
        }, world.Views.Select(v => (v.Scene, v.Camera, v.Toward, string.Join(',', v.Moments))));
        Assert.All(world.Views, v => Assert.Null(v.Picture));
    }

    [Fact]
    public async Task A_physical_change_to_the_place_between_a_cameras_beats_starts_another_view()
    {
        var diner = await DinerAsync();
        var parts = await ReadAsync(diner, signGoesOut: true);
        var (counter, entrance) = (Place(diner.Location, parts.Counter), Place(diner.Location, parts.Entrance));

        var world = await WorldAsync(diner);

        Assert.Equal(new[] { (counter, 0, "1"), (Place(diner.Location, parts.Booths), 0, "2,4"), (counter, 1, "3") },
            world.Views.Select(v => (v.Camera, v.State, string.Join(',', v.Moments))));
        await PictureAsync(diner, counter, entrance);
        var after = await PictureAsync(diner, counter, entrance, state: 1);
        var call = Call(after.Picture.Prompt);
        Assert.EndsWith("This is the same camera before the changes listed below.", call.References[0].Caption);
        Assert.Contains("Changed since this camera last saw the place:\n- neon sign: lit is now off\n", call.Prompt);
        Assert.Contains("- neon sign (fixture), at entrance; lit: off\n", call.Prompt);
    }

    [Fact]
    public async Task A_view_is_pictured_from_the_places_description()
    {
        var diner = await DinerAsync();
        var parts = await ReadAsync(diner);
        await DescribeAsync(diner, "A narrow 1950s diner of chrome and red vinyl.", "Counter: a chrome-edged bar with six stools.");

        var world = await WorldAsync(diner);
        var reply = await PictureAsync(diner, Place(diner.Location, parts.Counter), Place(diner.Location, parts.Entrance));

        var location = world.Locations.Single(l => l.Id == diner.Location.ToString());
        Assert.Equal(("A narrow 1950s diner of chrome and red vinyl.", "Counter: a chrome-edged bar with six stools."), (location.Description, location.Contents));
        Assert.Contains("What the place is: A narrow 1950s diner of chrome and red vinyl.\nWhat is in it: Counter: a chrome-edged bar with six stools.\n", reply.Picture.Prompt);
    }

    [Fact]
    public async Task A_view_is_pictured_as_the_place_with_no_one_in_it_and_kept()
    {
        var diner = await DinerAsync();
        var parts = await ReadAsync(diner);

        var reply = await PictureAsync(diner, Place(diner.Location, parts.Counter), Place(diner.Location, parts.Entrance));

        Assert.Equal(RecordingImageGenerator.Picture, reply.Image.ToByteArray());
        Assert.Equal(("image/webp", 10_000L, "Kept the counter in view."), (reply.Picture.ContentType, reply.Picture.CostUsdMicros, reply.Picture.Notes));
        var prompt = reply.Picture.Prompt;
        Assert.Empty(Call(prompt).References);
        Assert.Contains($"Place: {diner.Name}, ", prompt);
        Assert.Contains("Time of day: night.", prompt);
        Assert.Contains("The camera stands at counter (a long chrome-edged bar), looking toward entrance.", prompt);
        Assert.Contains("Its parts:\n- booths\n- counter (a long chrome-edged bar)\n- entrance\n", prompt);
        Assert.Contains("- spilled coffee (dressing), at counter\n", prompt);
        Assert.Contains("- neon sign (fixture), at entrance; lit: flickering\n", prompt);
        Assert.Contains("Ways out:\n- entrance opens onto PARKING LOT, through glass door\n", prompt);
        Assert.Contains("an empty set", prompt);
        foreach (var absent in new[] { "MAE", "rag", "coffee pot", "truckers", "wipes", "closed" })
        {
            Assert.DoesNotContain(absent, prompt);
        }

        var world = await WorldAsync(diner);
        Assert.Equal(reply.Picture, world.Views[0].Picture);
        Assert.Null(world.Views[1].Picture);
        var stored = await _theplot.Call<TheplotWorldModelsService, GetViewPictureReply>(
            (s, c) => s.GetViewPicture(new GetViewPictureRequest { StoryId = diner.StoryId, PictureId = reply.Picture.Id }, c));
        Assert.Equal(RecordingImageGenerator.Picture, stored.Image.ToByteArray());
        Assert.Equal("image/webp", stored.ContentType);
    }

    [Fact]
    public async Task A_place_is_pictured_from_its_other_views_pictures()
    {
        var diner = await DinerAsync();
        var parts = await ReadAsync(diner);
        await PictureAsync(diner, Place(diner.Location, parts.Counter), Place(diner.Location, parts.Entrance));

        var reverse = await PictureAsync(diner, Place(diner.Location, parts.Booths), Place(diner.Location, parts.Counter));

        var reference = Assert.Single(Call(reverse.Picture.Prompt).References);
        var caption = $"{diner.Name} in scene 1 (night): The camera stands at counter (a long chrome-edged bar), looking toward entrance.";
        Assert.Equal(RecordingImageGenerator.Picture, reference.Bytes);
        Assert.Equal(caption, reference.Caption);
        Assert.StartsWith($"[Reference picture] {caption}\n\n", reverse.Picture.Prompt);
        Assert.Contains("The pictures above show this same place from other views.", reverse.Picture.Prompt);
        Assert.Contains("The camera stands at booths, looking toward counter (a long chrome-edged bar).", reverse.Picture.Prompt);
    }

    [Fact]
    public async Task A_view_that_looks_nowhere_frames_where_its_beats_play_and_a_turn_from_its_spot_is_no_copy()
    {
        var diner = await DinerAsync();
        var parts = await ReadAsync(diner, aimless: true);
        var counter = Place(diner.Location, parts.Counter);

        var close = await PictureAsync(diner, counter, null);
        var turned = await PictureAsync(diner, counter, Place(diner.Location, parts.Entrance));

        Assert.Contains("The camera stands at counter (a long chrome-edged bar), looking at counter close up.\n"
                        + $"Counter fills the frame, not the rest of {diner.Name} beyond it.\n", close.Picture.Prompt);
        var call = Call(turned.Picture.Prompt);
        Assert.EndsWith(": The camera stands at counter (a long chrome-edged bar), looking at counter close up.", Assert.Single(call.References).Caption);
        Assert.Contains("looking toward entrance.\nEntrance fills the frame.\n"
                        + "The picture above from this same spot faces elsewhere: don't repeat its framing.\n", call.Prompt);
    }

    [Fact]
    public async Task A_picture_stays_with_its_view_through_a_build_that_names_the_parts_again()
    {
        var diner = await DinerAsync();
        var parts = await ReadAsync(diner);
        var first = await PictureAsync(diner, Place(diner.Location, parts.Counter), Place(diner.Location, parts.Entrance));

        await ClearAsync(diner);
        var again = await ReadAsync(diner);
        var rebuilt = await WorldAsync(diner);

        Assert.NotEqual(parts.Counter, again.Counter);
        Assert.Equal(Place(diner.Location, again.Counter), rebuilt.Views[0].Camera);
        Assert.Equal(first.Picture, rebuilt.Views[0].Picture);

        await ClearAsync(diner);
        await ReadAsync(diner, counter: "bar");
        Assert.All((await WorldAsync(diner)).Views, v => Assert.Null(v.Picture));
    }

    [Fact]
    public async Task Picturing_a_view_again_replaces_its_picture()
    {
        var diner = await DinerAsync();
        var parts = await ReadAsync(diner);
        var first = await PictureAsync(diner, Place(diner.Location, parts.Counter), Place(diner.Location, parts.Entrance));

        var second = await PictureAsync(diner, Place(diner.Location, parts.Counter), Place(diner.Location, parts.Entrance));

        Assert.Equal(first.Picture.Id, second.Picture.Id);
        await using var scope = database.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ThePlotDbContext>().ViewPictures.IgnoreQueryFilters()
            .CountAsync(p => p.StoryId == Guid.Parse(diner.StoryId), TestContext.Current.CancellationToken);
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task A_view_the_world_has_not_got_and_another_storys_picture_are_not_found()
    {
        var diner = await DinerAsync();
        var parts = await ReadAsync(diner);
        var other = await _writer.CreateStoryAsync();
        var picture = await PictureAsync(diner, Place(diner.Location, parts.Counter), Place(diner.Location, parts.Entrance));

        var view = await Assert.ThrowsAsync<RpcException>(() =>
            PictureAsync(diner, Place(diner.Location, parts.Counter), Place(diner.Location, parts.Booths)));
        var elsewhere = await Assert.ThrowsAsync<RpcException>(() => _theplot.Call<TheplotWorldModelsService, GetViewPictureReply>(
            (s, c) => s.GetViewPicture(new GetViewPictureRequest { StoryId = other.Id, PictureId = picture.Picture.Id }, c)));

        Assert.Equal((StatusCode.NotFound, StatusCode.NotFound), (view.StatusCode, elsewhere.StatusCode));
    }

    private (string Prompt, IReadOnlyList<ImageInput> References) Call(string transcript) =>
        database.Pictures.Calls.Single(c => transcript.EndsWith(c.Prompt, StringComparison.Ordinal));

    private Task<GetWorldReply> WorldAsync(Diner diner) => _theplot.Call<TheplotWorldModelsService, GetWorldReply>(
        (s, c) => s.GetWorld(new GetWorldRequest { StoryId = diner.StoryId }, c));

    private Task<PictureViewReply> PictureAsync(Diner diner, WorldPlace camera, WorldPlace? toward, int state = 0) =>
        _theplot.Call<TheplotWorldModelsService, PictureViewReply>((s, c) => s.PictureView(
            new PictureViewRequest { StoryId = diner.StoryId, SceneId = diner.SceneId, Camera = camera, Toward = toward, State = state }, c));

    private async Task DescribeAsync(Diner diner, string description, string contents)
    {
        await using var scope = database.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ThePlotDbContext>().Locations.IgnoreQueryFilters()
            .Where(l => l.Id == diner.Location)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.Description, description).SetProperty(l => l.Contents, contents));
    }

    private static WorldPlace Place(Guid location, Guid part) => new() { LocationId = location.ToString(), PartId = part.ToString() };

    /// <summary>One night scene in a diner whose name no other test's prompt shares.</summary>
    private async Task<Diner> DinerAsync()
    {
        var story = await _writer.CreateStoryAsync();
        var name = $"DINER {Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var diner = await _writer.CreateLocationAsync(story.Id, name);
        var lot = await _writer.CreateLocationAsync(story.Id, "PARKING LOT");
        var mae = await _writer.CreateCharacterAsync(story.Id, "MAE");
        var cal = await _writer.CreateCharacterAsync(story.Id, "CAL");
        const string wipes = "@MAE wipes the counter.";
        var scene = await _writer.WriteFirstSceneAsync(story,
            Heading(diner.Id, "NIGHT"), Action(wipes, Mention(wipes, "@MAE", mae)), Dialogue(mae.Id, "We're closed."),
            Dialogue(cal.Id, "Door says open."), Action("Truckers drift in."));
        return new Diner(
            story.Id, scene.Id, name, Guid.Parse(diner.Id), Guid.Parse(lot.Id), Guid.Parse(mae.Id),
            scene.Elements.Skip(1).Select(e => Guid.Parse(e.Id)).ToArray());
    }

    /// <summary>What a build reads from the scene, with new parts and objects each time.</summary>
    /// <param name="signGoesOut">The neon sign goes out at the second beat, between the counter camera's two.</param>
    /// <param name="aimless">The counter camera's first shot doesn't say where it looks.</param>
    private async Task<DinerParts> ReadAsync(Diner diner, string counter = "counter", bool signGoesOut = false, bool aimless = false)
    {
        var parts = new DinerParts(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var storyId = Guid.Parse(diner.StoryId);
        LocationObject Thing(string name, ObjectKind kind) =>
            new() { Id = Guid.NewGuid(), UserId = _writer.UserId, StoryId = storyId, Name = name, Kind = kind };
        var (pot, rag, coffee, sign, door, truckers) = (Thing("coffee pot", ObjectKind.Prop), Thing("rag", ObjectKind.Prop),
            Thing("spilled coffee", ObjectKind.Dressing), Thing("neon sign", ObjectKind.Fixture), Thing("glass door", ObjectKind.Fixture),
            Thing("truckers", ObjectKind.Occupant));
        var (b1, b2, b3, b4) = (diner.Beats[0], diner.Beats[1], diner.Beats[2], diner.Beats[3]);
        var location = diner.Location;
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(_writer.UserId);
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateReadWrite("worker");
        var partRows = scope.ServiceProvider.GetRequiredService<ILocationPartRepository>();
        await partRows.AddAsync(new LocationPart { Id = parts.Counter, LocationId = location, Name = counter, Description = "a long chrome-edged bar" }, default);
        await partRows.AddAsync(new LocationPart { Id = parts.Booths, LocationId = location, Name = "booths" }, default);
        await partRows.AddAsync(new LocationPart { Id = parts.Entrance, LocationId = location, Name = "entrance" }, default);
        foreach (var thing in new[] { pot, rag, coffee, sign, door, truckers })
        {
            await scope.ServiceProvider.GetRequiredService<ILocationObjectRepository>().AddAsync(thing, default);
        }

        var observations = scope.ServiceProvider.GetRequiredService<ILocationObservationRepository>();
        foreach (var observation in new[]
                 {
                     Observe(b1, ObservationEdge.BeatAt, location, parts.Counter),
                     Observe(b1, ObservationEdge.Camera, location, parts.Counter, other: aimless ? null : (location, parts.Entrance)),
                     Observe(b1, ObservationEdge.CharacterAt, location, parts.Counter, character: diner.Mae),
                     Observe(b1, ObservationEdge.At, location, parts.Counter, character: diner.Mae, @object: rag.Id),
                     Observe(b1, ObservationEdge.At, location, parts.Counter, @object: pot.Id),
                     Observe(b1, ObservationEdge.At, location, parts.Counter, @object: coffee.Id),
                     Observe(b1, ObservationEdge.At, location, parts.Entrance, @object: sign.Id),
                     Observe(b1, ObservationEdge.State, null, null, @object: sign.Id, state: ("lit", "flickering")),
                     Observe(b1, ObservationEdge.At, location, parts.Entrance, @object: door.Id),
                     Observe(b1, ObservationEdge.At, location, parts.Booths, @object: truckers.Id),
                     Observe(b1, ObservationEdge.Connects, location, parts.Entrance, other: (diner.Lot, null), via: door.Id),
                     Observe(b2, ObservationEdge.Camera, location, parts.Booths, other: (location, parts.Counter)),
                     Observe(b2, ObservationEdge.At, location, parts.Booths, @object: pot.Id),
                     Observe(b3, ObservationEdge.Camera, location, parts.Counter, other: (location, parts.Entrance)),
                     Observe(b3, ObservationEdge.CharacterAt, location, parts.Booths, character: diner.Mae),
                     Observe(b4, ObservationEdge.Camera, location, parts.Booths, other: (location, parts.Counter)),
                 })
        {
            await observations.AddAsync(observation, default);
        }

        if (signGoesOut)
        {
            await observations.AddAsync(Observe(b2, ObservationEdge.State, null, null, @object: sign.Id, state: ("lit", "off")), default);
        }

        await unitOfWork.CommitAsync(default);
        return parts;
    }

    private async Task ClearAsync(Diner diner)
    {
        await using var scope = database.Services.CreateAsyncScope();
        var theplot = scope.ServiceProvider.GetRequiredService<ThePlotDbContext>();
        var storyId = Guid.Parse(diner.StoryId);
        await theplot.LocationObservations.IgnoreQueryFilters().Where(o => diner.Beats.Contains(o.SceneElementId)).ExecuteDeleteAsync();
        await theplot.LocationObjects.IgnoreQueryFilters().Where(o => o.StoryId == storyId).ExecuteDeleteAsync();
        await theplot.LocationParts.IgnoreQueryFilters().Where(p => p.LocationId == diner.Location).ExecuteDeleteAsync();
    }

    private static LocationObservation Observe(
        Guid beat, ObservationEdge edge, Guid? location, Guid? part, Guid? character = null, Guid? @object = null,
        (Guid Location, Guid? Part)? other = null, (string Property, string Value)? state = null, Guid? via = null) => new()
    {
        Id = Guid.NewGuid(), SceneElementId = beat, Edge = edge, PlaceLocationId = location, PlacePartId = part,
        CharacterId = character, ObjectId = @object, OtherLocationId = other?.Location, OtherPartId = other?.Part, ViaObjectId = via,
        Property = state?.Property, Value = state?.Value, Basis = ObservationBasis.Stated, Origin = ObservationOrigin.Extracted,
    };

    private sealed record Diner(string StoryId, string SceneId, string Name, Guid Location, Guid Lot, Guid Mae, Guid[] Beats);

    private sealed record DinerParts(Guid Counter, Guid Booths, Guid Entrance);
}
