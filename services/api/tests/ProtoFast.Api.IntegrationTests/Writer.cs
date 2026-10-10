using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Jobs;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Database.Abstractions;
using Xunit;

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

    public async Task<TReply> Direct<TReply>(Func<DirectionService, ServerCallContext, Task<TReply>> call)
    {
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(UserId);
        ScriptedLanguageModelFactory.User.Value = UserId;
        return await call(scope.ServiceProvider.GetRequiredService<DirectionService>(), new TestServerCallContext());
    }

    public async Task<TReply> Jobs<TReply>(Func<DirectorJobsService, ServerCallContext, Task<TReply>> call)
    {
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(UserId);
        return await call(scope.ServiceProvider.GetRequiredService<DirectorJobsService>(), new TestServerCallContext());
    }

    /// <summary>Claims and runs this writer's next runnable job, as the worker would; false when none is.</summary>
    public async Task<bool> RunNextJobAsync()
    {
        var store = database.Services.GetRequiredService<DirectorJobStore>();
        if (await store.ClaimAsync(TestContext.Current.CancellationToken, UserId) is not { } job)
        {
            return false;
        }

        ScriptedLanguageModelFactory.User.Value = UserId;
        await database.Services.GetRequiredService<DirectorJobRunner>().RunAsync(job, CancellationToken.None);
        return true;
    }

    /// <summary>What the language model answers, in order, to this writer's requests.</summary>
    public void ScriptModel(params string[] replies) =>
        database.Services.GetRequiredService<ScriptedLanguageModelFactory>().Enqueue(UserId, replies);

    public IReadOnlyList<string> ModelPrompts() =>
        database.Services.GetRequiredService<ScriptedLanguageModelFactory>().Prompts(UserId);

    public Task<SceneDirection> GetDirectionAsync(string sceneId) =>
        Direct(async (d, c) => (await d.GetSceneDirection(new GetSceneDirectionRequest { SceneId = sceneId }, c)).Direction);

    public Task<SceneDirection> SaveDirectionAsync(SceneDirection direction) =>
        Direct(async (d, c) => (await d.SaveSceneDirection(new SaveSceneDirectionRequest { Direction = direction }, c)).Direction);

    public Task<SceneDirection> DirectAsync(DirectRequest request) =>
        Direct(async (d, c) => (await d.Direct(request, c)).Direction);

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

    public async Task<Extra> CreateExtraAsync(string storyId, string name, string? description = null, string? wardrobe = null, int? headcount = null)
    {
        var request = new CreateExtraRequest { StoryId = storyId, Name = name };
        if (description is not null)
        {
            request.Description = description;
        }

        if (wardrobe is not null)
        {
            request.Wardrobe = wardrobe;
        }

        if (headcount is not null)
        {
            request.Headcount = headcount.Value;
        }

        return (await Call((s, c) => s.CreateExtra(request, c))).Extra;
    }

    public async Task<Prop> CreatePropAsync(string storyId, string name) =>
        (await Call((s, c) => s.CreateProp(new CreatePropRequest { StoryId = storyId, Name = name }, c))).Prop;
}
