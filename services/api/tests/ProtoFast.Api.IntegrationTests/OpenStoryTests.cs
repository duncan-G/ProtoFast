using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Services.OpenStories;
using ProtoFast.Data.ThePlot;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using Xunit;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using ContainerRecord = ProtoFast.Data.ThePlot.Entities.Container;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using LocationSettingRecord = ProtoFast.Data.ThePlot.Entities.LocationSetting;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>A writer finding the open library's stories and copying one onto their desk.</summary>
public sealed class OpenStoryTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    [Fact]
    public async Task A_writer_imports_an_open_story_as_their_own_copy_once()
    {
        var openId = await SeedOpenStoryAsync();

        var listed = Assert.Single((await ListAsync()).Stories, s => s.Id == openId.ToString());
        Assert.Equal(("The Crossing", "Boston. April 1775.", 2, 2, ""), (listed.Title, listed.Opening, listed.SceneCount, listed.CharacterCount, listed.ImportedStoryId));

        var imported = await ImportAsync(openId);
        Assert.True(imported.Created);
        Assert.Equal("The Crossing", imported.Story.Title);

        var copy = await _writer.GetStoryAsync(imported.Story.Id);
        Assert.Equal(["Act I"], copy.Containers.Select(c => c.Label));
        Assert.Equal(["Signal", "Oars"], copy.Containers[0].Scenes.Select(s => s.Title));
        Assert.Equal(["MORNING"], copy.Vocabulary.TimesOfDay);
        var hale = Assert.Single(copy.Characters, c => c.Name == "Captain Hale");
        var steeple = Assert.Single(copy.Locations, l => l.Name == "OLD NORTH STEEPLE");
        var lantern = Assert.Single(copy.Props);

        var oars = await _writer.GetSceneAsync(copy.Containers[0].Scenes[1].Id);
        Assert.Equal(
            [SceneElementType.Heading, SceneElementType.Action, SceneElementType.Dialogue],
            oars.Elements.Select(e => e.Type));
        Assert.Equal(hale.Id, oars.Elements[2].SpeakerId);
        var signal = await _writer.GetSceneAsync(copy.Containers[0].Scenes[0].Id);
        Assert.Equal(steeple.Id, signal.Elements[0].LocationId);
        var mentions = signal.Elements[2].Mentions;
        Assert.Equal([(hale.Id, "", 0), ("", lantern.Id, 20)], mentions.Select(m => (m.CharacterId, m.PropId, m.Offset)));

        await using (var scope = database.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ThePlotDbContext>();
            var copied = await db.Stories.IgnoreQueryFilters().Include(s => s.Locations).SingleAsync(s => s.Id == Guid.Parse(copy.Id), TestContext.Current.CancellationToken);
            Assert.Equal((_writer.UserId, openId), (copied.UserId, copied.SourceStoryId));
            var tower = copied.Locations.Single(l => l.Name == "OLD NORTH STEEPLE");
            Assert.Equal(copied.Locations.Single(l => l.Name == "OLD NORTH CHURCH").Id, tower.InsideLocationId);
            Assert.Equal(1, await db.Stories.IgnoreQueryFilters().CountAsync(s => s.Id == openId && s.UserId == OpenLibrary.OwnerId, TestContext.Current.CancellationToken));
        }

        var again = await ImportAsync(openId);
        Assert.Equal((false, imported.Story.Id), (again.Created, again.Story.Id));
        Assert.Equal(imported.Story.Id, Assert.Single((await ListAsync()).Stories, s => s.Id == openId.ToString()).ImportedStoryId);
        Assert.Equal("", Assert.Single((await ListAsync(new Writer(database))).Stories, s => s.Id == openId.ToString()).ImportedStoryId);
    }

    [Fact]
    public async Task Another_writers_story_is_not_open_and_cannot_be_imported()
    {
        var other = await new Writer(database).CreateStoryAsync("Private Pages");

        Assert.DoesNotContain((await ListAsync()).Stories, s => s.Id == other.Id);
        var refused = await Assert.ThrowsAsync<RpcException>(() => ImportAsync(Guid.Parse(other.Id)));
        Assert.Equal(StatusCode.NotFound, refused.StatusCode);
    }

    private Task<ListOpenStoriesReply> ListAsync(Writer? writer = null) =>
        (writer ?? _writer).Call<OpenStoryService, ListOpenStoriesReply>((s, c) => s.ListOpenStories(new ListOpenStoriesRequest(), c));

    private Task<ImportOpenStoryReply> ImportAsync(Guid openId) =>
        _writer.Call<OpenStoryService, ImportOpenStoryReply>(
            (s, c) => s.ImportOpenStory(new ImportOpenStoryRequest { OpenStoryId = openId.ToString() }, c));

    private async Task<Guid> SeedOpenStoryAsync()
    {
        var hale = new CharacterRecord { Name = "Captain Hale", Kind = "Human", Hue = 30 };
        var ferryman = new CharacterRecord { Name = "Ferryman", Kind = "Human", Hue = 90 };
        var church = new LocationRecord { Name = "OLD NORTH CHURCH", Setting = LocationSettingRecord.Exterior, Hue = 10 };
        var steeple = new LocationRecord { Name = "OLD NORTH STEEPLE", Setting = LocationSettingRecord.Exterior, Hue = 20, InsideLocation = church };
        var river = new LocationRecord { Name = "THE RIVER", Setting = LocationSettingRecord.Exterior, Hue = 200 };
        var lantern = new PropRecord { Name = "lantern" };
        var story = new StoryRecord
        {
            Title = "The Crossing",
            Characters = [hale, ferryman],
            Locations = [church, steeple, river],
            Props = [lantern],
            Containers =
            [
                new ContainerRecord
                {
                    Label = "Act I",
                    Scenes =
                    [
                        new SceneRecord
                        {
                            Title = "Signal", Position = 0,
                            Elements =
                            [
                                new SceneElementRecord { Position = 0, Type = SceneElementTypeRecord.Heading, Location = steeple, TimeOfDay = "NIGHT" },
                                new SceneElementRecord { Position = 1, Type = SceneElementTypeRecord.Action, Text = "SUPER: Boston. April 1775." },
                                new SceneElementRecord
                                {
                                    Position = 2, Type = SceneElementTypeRecord.Action, Text = "@Captain Hale lifts the @lantern.",
                                    Mentions =
                                    [
                                        new SceneElementMentionRecord { Character = hale, Offset = 0, Length = 13 },
                                        new SceneElementMentionRecord { Prop = lantern, Offset = 20, Length = 8 },
                                    ],
                                },
                            ],
                        },
                        new SceneRecord
                        {
                            Title = "Oars", Position = 1,
                            Elements =
                            [
                                new SceneElementRecord { Position = 0, Type = SceneElementTypeRecord.Heading, Location = river, TimeOfDay = "NIGHT" },
                                new SceneElementRecord { Position = 1, Type = SceneElementTypeRecord.Action, Text = "Muffled oars dip." },
                                new SceneElementRecord { Position = 2, Type = SceneElementTypeRecord.Dialogue, Speaker = hale, Text = "Not a sound." },
                            ],
                        },
                    ],
                },
            ],
        };
        story.Vocabulary.TimesOfDay.Add("MORNING");

        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(OpenLibrary.OwnerId);
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateReadWrite("seed");
        await scope.ServiceProvider.GetRequiredService<IStoryRepository>().AddAsync(story, TestContext.Current.CancellationToken);
        await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);
        return story.Id;
    }
}
