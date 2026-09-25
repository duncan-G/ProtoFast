using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>A fresh user; each call runs in its own scope, as a gRPC call would.</summary>
public sealed class Writer(StoryDatabase database)
{
    public string UserId { get; } = $"writer-{Guid.NewGuid():N}";

    public async Task<TReply> Call<TReply>(Func<StoryService, ServerCallContext, Task<TReply>> call)
    {
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(UserId);
        return await call(scope.ServiceProvider.GetRequiredService<StoryService>(), new TestServerCallContext());
    }

    public async Task<Story> CreateStoryAsync(string title = "The Signal in the Scrap") =>
        (await Call((s, c) => s.CreateStory(new CreateStoryRequest { Title = title }, c))).Story;

    public async Task<Story> GetStoryAsync(string storyId) =>
        (await Call((s, c) => s.GetStory(new GetStoryRequest { StoryId = storyId }, c))).Story;

    public async Task<Scene> GetSceneAsync(string sceneId) =>
        (await Call((s, c) => s.GetScene(new GetSceneRequest { SceneId = sceneId }, c))).Scene;

    public Task<SaveSceneReply> SaveSceneAsync(Scene scene) =>
        Call((s, c) => s.SaveScene(new SaveSceneRequest { Scene = scene }, c));

    /// <summary>Replaces the story's first scene with these rows and reads it back.</summary>
    public async Task<Scene> WriteFirstSceneAsync(Story story, params SceneElement[] rows)
    {
        var scene = await GetSceneAsync(story.Containers[0].Scenes[0].Id);
        scene.Elements.Clear();
        scene.Elements.AddRange(rows);
        await SaveSceneAsync(scene);
        return await GetSceneAsync(scene.Id);
    }

    public async Task<Scene> CreateSceneAsync(string containerId, int position, string title, params SceneElement[] rows)
    {
        var scene = new Scene { Id = Guid.NewGuid().ToString(), ContainerId = containerId, Position = position, Title = title };
        scene.Elements.AddRange(rows);
        return (await Call((s, c) => s.CreateScene(new CreateSceneRequest { Scene = scene }, c))).Scene;
    }

    public async Task<Character> CreateCharacterAsync(string storyId, string name, string kind = "Human") =>
        (await Call((s, c) => s.CreateCharacter(
            new CreateCharacterRequest { StoryId = storyId, Name = name, Kind = kind, Hue = 25 }, c))).Character;

    public async Task<Location> CreateLocationAsync(string storyId, string name) =>
        (await Call((s, c) => s.CreateLocation(
            new CreateLocationRequest { StoryId = storyId, Name = name, Setting = LocationSetting.Exterior, Hue = 200 },
            c))).Location;

    public async Task<Prop> CreatePropAsync(string storyId, string name) =>
        (await Call((s, c) => s.CreateProp(new CreatePropRequest { StoryId = storyId, Name = name }, c))).Prop;
}
