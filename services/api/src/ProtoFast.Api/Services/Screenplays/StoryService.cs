using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ContainerRecord = ProtoFast.Data.ThePlot.Entities.Container;
using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Screenplays;

/// <summary>
/// The caller's stories. Every query runs under the user context the gRPC interceptor set, so
/// another user's ids read as not found.
/// </summary>
public class StoryService(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    StoryLibrary library,
    IStoryRepository storyRepository,
    IQueryFactory<StoryRecord, IStoryQuery> storyQueries,
    IContainerRepository containerRepository,
    IQueryFactory<ContainerRecord, IContainerQuery> containerQueries,
    ISceneRepository sceneRepository,
    IQueryFactory<SceneRecord, ISceneQuery> sceneQueries,
    ISceneElementRepository sceneElementRepository,
    IQueryFactory<SceneElementRecord, ISceneElementQuery> sceneElementQueries) : Stories.StoriesBase
{
    private static readonly IReadOnlyDictionary<Guid, SceneElementRecord> NoElements =
        new Dictionary<Guid, SceneElementRecord>();

    public override async Task<ListStoriesReply> ListStories(ListStoriesRequest request, ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListStories));
        var stories = await storyRepository.GetByQueryAsync(
            storyQueries.Create().RecentlyModifiedFirst(), context.CancellationToken);

        var reply = new ListStoriesReply();
        reply.Stories.AddRange(stories.Select(StoryMessages.ToSummary));
        return reply;
    }

    public override async Task<CreateStoryReply> CreateStory(CreateStoryRequest request, ServerCallContext context)
    {
        var story = new StoryRecord
        {
            Title = OutlineText.StoryTitle(request.Title),
            Containers =
            [
                new ContainerRecord
                {
                    Label = OutlineText.FirstContainer,
                    Scenes =
                    [
                        new SceneRecord
                        {
                            Title = OutlineText.UntitledScene,
                            Elements = [new SceneElementRecord { Type = SceneElementTypeRecord.Heading }],
                        },
                    ],
                },
            ],
        };

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateStory));
        await storyRepository.AddAsync(story, context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new CreateStoryReply { Story = StoryMessages.ToMessage(story, NoElements) };
    }

    public override async Task<GetStoryReply> GetStory(GetStoryRequest request, ServerCallContext context)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(GetStory));
        var story = await storyRepository.GetFirstByQueryAsync(
                        storyQueries.Create().WithId(storyId).WithOutline().WithLibrary(),
                        context.CancellationToken)
                    ?? throw StoryErrors.NotFound(StoryErrors.StoryNotFound);

        var headings = await sceneElementRepository.GetByQueryAsync(
            sceneElementQueries.Create().InStory(storyId).OfType(SceneElementTypeRecord.Heading).InOrder(),
            e => new SceneElementRecord { SceneId = e.SceneId, LocationId = e.LocationId, TimeOfDay = e.TimeOfDay },
            context.CancellationToken);
        var openings = headings.GroupBy(h => h.SceneId).ToDictionary(g => g.Key, g => g.First());

        return new GetStoryReply { Story = StoryMessages.ToMessage(story, openings) };
    }

    public override async Task<UpdateStoryReply> UpdateStory(UpdateStoryRequest request, ServerCallContext context)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        var title = OutlineText.StoryTitle(request.Title);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateStory));
        await storyScope.LockAsync(storyId, context.CancellationToken);

        var story = await storyScope.GetAsync(storyId, context.CancellationToken);
        story.Title = title;
        await unitOfWork.CommitAsync(context.CancellationToken);
        return new UpdateStoryReply();
    }

    public override async Task<DeleteStoryReply> DeleteStory(DeleteStoryRequest request, ServerCallContext context)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteStory));
        var deleted = await storyRepository.DeleteByQueryAsync(
            storyQueries.Create().WithId(storyId), context.CancellationToken);
        if (deleted == 0)
        {
            throw StoryErrors.NotFound(StoryErrors.StoryNotFound);
        }

        await unitOfWork.CommitAsync(context.CancellationToken);
        return new DeleteStoryReply();
    }

    public override async Task<GetSceneReply> GetScene(GetSceneRequest request, ServerCallContext context)
    {
        var sceneId = StoryIds.Existing(request.SceneId, StoryErrors.SceneNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(GetScene));
        var scene = await sceneRepository.GetFirstByQueryAsync(
                        sceneQueries.Create().WithId(sceneId).WithElements(), context.CancellationToken)
                    ?? throw StoryErrors.NotFound(StoryErrors.SceneNotFound);

        return new GetSceneReply { Scene = StoryMessages.ToMessage(scene) };
    }

    /// <summary>Elements are upserted by id and any the request leaves out are deleted.</summary>
    public override async Task<SaveSceneReply> SaveScene(SaveSceneRequest request, ServerCallContext context)
    {
        var message = request.Scene ?? throw StoryErrors.Invalid("There’s no scene to save.");
        var sceneId = StoryIds.Existing(message.Id, StoryErrors.SceneNotFound);
        var title = OutlineText.SceneTitle(message.Title);
        var cancellationToken = context.CancellationToken;

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(SaveScene));
        var storyId = await sceneRepository.GetFirstByQueryAsync(
                          sceneQueries.Create().WithId(sceneId), s => (Guid?)s.Container.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(StoryErrors.SceneNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var scene = await sceneRepository.GetFirstByQueryAsync(
                        sceneQueries.Create().WithId(sceneId).WithElements(), cancellationToken)
                    ?? throw StoryErrors.NotFound(StoryErrors.SceneNotFound);
        var references = await storyScope.ReferencesAsync(storyId, cancellationToken);
        var stored = scene.Elements.ToDictionary(e => e.Id);
        var elements = SceneValidator.Validate(message.Elements, references, stored);

        scene.Title = title;
        var kept = elements.Select(e => e.Id).ToHashSet();
        foreach (var stale in scene.Elements.Where(e => !kept.Contains(e.Id)).ToList())
        {
            await sceneElementRepository.RemoveAsync(stale, cancellationToken);
        }

        foreach (var element in elements)
        {
            if (stored.TryGetValue(element.Id, out var existing))
            {
                Overwrite(existing, element);
            }
            else
            {
                element.SceneId = scene.Id;
                await sceneElementRepository.AddAsync(element, cancellationToken);
            }
        }

        await CommitAsync(unitOfWork, "Another scene already uses one of these rows.", cancellationToken);
        return new SaveSceneReply();
    }

    public override async Task<CreateSceneReply> CreateScene(CreateSceneRequest request, ServerCallContext context)
    {
        var message = request.Scene ?? throw StoryErrors.Invalid("There’s no scene to add.");
        var containerId = StoryIds.Existing(message.ContainerId, StoryErrors.ContainerNotFound);
        var sceneId = StoryIds.New(message.Id, "The scene has no valid id.");
        var title = OutlineText.SceneTitle(message.Title);
        var cancellationToken = context.CancellationToken;

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateScene));
        var storyId = await containerRepository.GetFirstByQueryAsync(
                          containerQueries.Create().WithId(containerId), c => (Guid?)c.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(StoryErrors.ContainerNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var references = await storyScope.ReferencesAsync(storyId, cancellationToken);
        var elements = SceneValidator.Validate(message.Elements, references, NoElements);
        var siblings = await sceneRepository.GetByQueryAsync(
            sceneQueries.Create().InContainer(containerId), s => s.Id, cancellationToken);
        var position = Math.Clamp(message.Position, 0, siblings.Count);
        await sceneRepository.UpdateByQueryAsync(
            sceneQueries.Create().InContainer(containerId).AtOrAfterPosition(position),
            s => s.SetProperty(scene => scene.Position, scene => scene.Position + 1),
            cancellationToken);

        var scene = new SceneRecord
        {
            Id = sceneId,
            ContainerId = containerId,
            Position = position,
            Title = title,
            Elements = elements,
        };
        await sceneRepository.AddAsync(scene, cancellationToken);
        await CommitAsync(unitOfWork, "That scene or one of its rows already exists.", cancellationToken);

        return new CreateSceneReply { Scene = StoryMessages.ToMessage(scene) };
    }

    public override async Task<CreateContainerReply> CreateContainer(
        CreateContainerRequest request,
        ServerCallContext context)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        var label = OutlineText.ContainerLabel(request.Label);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateContainer));
        await storyScope.LockAsync(storyId, context.CancellationToken);

        var positions = await containerRepository.GetByQueryAsync(
            containerQueries.Create().InStory(storyId), c => c.Position, context.CancellationToken);
        var container = new ContainerRecord
        {
            StoryId = storyId,
            Label = label,
            Position = positions.Count == 0 ? 0 : positions.Max() + 1,
        };
        await containerRepository.AddAsync(container, context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new CreateContainerReply { Container = StoryMessages.ToMessage(container, NoElements) };
    }

    public override async Task<RenameContainerReply> RenameContainer(
        RenameContainerRequest request,
        ServerCallContext context)
    {
        var containerId = StoryIds.Existing(request.ContainerId, StoryErrors.ContainerNotFound);
        var label = OutlineText.ContainerLabel(request.Label);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(RenameContainer));
        var storyId = await containerRepository.GetFirstByQueryAsync(
                          containerQueries.Create().WithId(containerId), c => (Guid?)c.StoryId, context.CancellationToken)
                      ?? throw StoryErrors.NotFound(StoryErrors.ContainerNotFound);
        await storyScope.LockAsync(storyId, context.CancellationToken);

        var container = await containerRepository.GetFirstByQueryAsync(
                            containerQueries.Create().WithId(containerId), context.CancellationToken)
                        ?? throw StoryErrors.NotFound(StoryErrors.ContainerNotFound);
        container.Label = label;
        await unitOfWork.CommitAsync(context.CancellationToken);
        return new RenameContainerReply();
    }

    public override async Task<CreateCharacterReply> CreateCharacter(
        CreateCharacterRequest request,
        ServerCallContext context) =>
        new() { Character = await library.CreateCharacterAsync(request, context.CancellationToken) };

    public override async Task<UpdateCharacterReply> UpdateCharacter(
        UpdateCharacterRequest request,
        ServerCallContext context)
    {
        await library.UpdateCharacterAsync(request, context.CancellationToken);
        return new UpdateCharacterReply();
    }

    public override async Task<DeleteCharacterReply> DeleteCharacter(
        DeleteCharacterRequest request,
        ServerCallContext context)
    {
        await library.DeleteCharacterAsync(request, context.CancellationToken);
        return new DeleteCharacterReply();
    }

    public override async Task<CreateLocationReply> CreateLocation(
        CreateLocationRequest request,
        ServerCallContext context) =>
        new() { Location = await library.CreateLocationAsync(request, context.CancellationToken) };

    public override async Task<UpdateLocationReply> UpdateLocation(
        UpdateLocationRequest request,
        ServerCallContext context)
    {
        await library.UpdateLocationAsync(request, context.CancellationToken);
        return new UpdateLocationReply();
    }

    public override async Task<DeleteLocationReply> DeleteLocation(
        DeleteLocationRequest request,
        ServerCallContext context)
    {
        await library.DeleteLocationAsync(request, context.CancellationToken);
        return new DeleteLocationReply();
    }

    public override async Task<CreatePropReply> CreateProp(CreatePropRequest request, ServerCallContext context) =>
        new() { Prop = await library.CreatePropAsync(request, context.CancellationToken) };

    public override async Task<UpdatePropReply> UpdateProp(UpdatePropRequest request, ServerCallContext context)
    {
        await library.UpdatePropAsync(request, context.CancellationToken);
        return new UpdatePropReply();
    }

    public override async Task<DeletePropReply> DeleteProp(DeletePropRequest request, ServerCallContext context)
    {
        await library.DeletePropAsync(request, context.CancellationToken);
        return new DeletePropReply();
    }

    public override async Task<SaveVocabularyReply> SaveVocabulary(
        SaveVocabularyRequest request,
        ServerCallContext context)
    {
        await library.SaveVocabularyAsync(request, context.CancellationToken);
        return new SaveVocabularyReply();
    }

    private static void Overwrite(SceneElementRecord stored, SceneElementRecord element)
    {
        stored.Position = element.Position;
        stored.Type = element.Type;
        stored.Text = element.Text;
        stored.LocationId = element.LocationId;
        stored.TimeOfDay = element.TimeOfDay;
        stored.SpeakerId = element.SpeakerId;
        stored.Parenthetical = element.Parenthetical;
        stored.Transition = element.Transition;
        if (!SameMentions(stored.Mentions, element.Mentions))
        {
            stored.Mentions.Clear();
            stored.Mentions.AddRange(element.Mentions);
        }
    }

    private static bool SameMentions(List<SceneElementMentionRecord> stored, List<SceneElementMentionRecord> next) =>
        stored.Count == next.Count
        && stored.OrderBy(m => m.Offset).Zip(next).All(pair =>
            pair.First.Offset == pair.Second.Offset
            && pair.First.Length == pair.Second.Length
            && pair.First.CharacterId == pair.Second.CharacterId
            && pair.First.LocationId == pair.Second.LocationId
            && pair.First.PropId == pair.Second.PropId);

    /// <summary>Client-picked ids can collide with rows the caller cannot see.</summary>
    private static async Task CommitAsync(IUnitOfWork unitOfWork, string collision, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (StoryErrors.IsUniqueViolation(exception))
        {
            throw StoryErrors.Invalid(collision);
        }
    }
}
