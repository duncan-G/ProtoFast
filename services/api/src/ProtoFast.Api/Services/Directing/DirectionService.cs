using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Api.Services.Speech;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Screenplay.Models;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// The director layer over a scene. Reads and author saves run like the story service's; a model
/// request reads its context without the story lock, asks the model, then takes the lock to write,
/// so typing in the editor is never held up by a model call.
/// </summary>
public class DirectionService(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    DirectionScope directionScope,
    SceneSaver sceneSaver,
    SceneDirector director,
    SheetDescriber describer,
    CueAudioGenerator audioGenerator,
    CueAudioMessages audioMessages,
    ISpeechSynthesizer speech,
    IStoryRepository storyRepository,
    IQueryFactory<StoryRecord, IStoryQuery> storyQueries,
    ICharacterRepository characterRepository,
    IQueryFactory<CharacterRecord, ICharacterQuery> characterQueries,
    ILocationRepository locationRepository,
    IQueryFactory<LocationRecord, ILocationQuery> locationQueries,
    IPropRepository propRepository,
    IQueryFactory<PropRecord, IPropQuery> propQueries,
    ISceneElementRepository sceneElementRepository,
    IQueryFactory<SceneElementRecord, ISceneElementQuery> sceneElementQueries,
    ILogger<DirectionService> logger) : Direction.DirectionBase
{
    private const string CharacterNotFound = "That character is no longer in the library.";
    private const string LocationNotFound = "That location is no longer in the library.";
    private const string PropNotFound = "That prop is no longer in the library.";

    public override async Task<GetSceneDirectionReply> GetSceneDirection(GetSceneDirectionRequest request, ServerCallContext context)
    {
        var sceneId = StoryIds.Existing(request.SceneId, StoryErrors.SceneNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(GetSceneDirection));
        var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, context.CancellationToken);
        return new GetSceneDirectionReply { Direction = ToMessage(directed) };
    }

    public override async Task<SaveSceneDirectionReply> SaveSceneDirection(
        SaveSceneDirectionRequest request, ServerCallContext context)
    {
        var message = request.Direction ?? throw StoryErrors.Invalid("There’s no direction to save.");
        var sceneId = StoryIds.Existing(message.SceneId, StoryErrors.SceneNotFound);
        var cancellationToken = context.CancellationToken;

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(SaveSceneDirection));
        var storyId = await directionScope.StoryIdAsync(sceneId, cancellationToken);
        await storyScope.LockAsync(storyId, cancellationToken);

        var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, cancellationToken);
        DirectionWriter.Apply(directed.Scene, DirectionValidator.Validate(message, directed.References), byAuthor: true);
        await CommitAsync(unitOfWork, cancellationToken);
        return new SaveSceneDirectionReply { Direction = ToMessage(directed) };
    }

    public override async Task<SaveDirectedSceneReply> SaveDirectedScene(
        SaveDirectedSceneRequest request, ServerCallContext context)
    {
        var sceneMessage = request.Scene ?? throw StoryErrors.Invalid("There’s no scene to save.");
        var message = request.Direction ?? throw StoryErrors.Invalid("There’s no direction to save.");
        if (message.SceneId != sceneMessage.Id)
        {
            throw StoryErrors.Invalid("The direction belongs to another scene.");
        }

        var cancellationToken = context.CancellationToken;
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(SaveDirectedScene));
        var storyId = await directionScope.StoryIdAsync(StoryIds.Existing(sceneMessage.Id, StoryErrors.SceneNotFound), cancellationToken);
        var scene = await sceneSaver.SaveAsync(sceneMessage, cancellationToken, includeDirection: true);
        var directed = await directionScope.LoadAsync(scene, storyId, withEarlierScenes: false, cancellationToken);
        DirectionWriter.Apply(scene, DirectionValidator.Validate(message, directed.References), byAuthor: true);
        await CommitAsync(unitOfWork, cancellationToken);
        return new SaveDirectedSceneReply { Direction = ToMessage(directed) };
    }

    public override async Task<DirectReply> Direct(DirectRequest request, ServerCallContext context)
    {
        var sceneId = StoryIds.Existing(request.SceneId, StoryErrors.SceneNotFound);
        var cancellationToken = context.CancellationToken;

        DirectionDraft draft;
        using (unitOfWorkFactory.CreateReadOnly(nameof(Direct)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: true, cancellationToken);
            draft = await AskAsync(() => director.DirectAsync(directed, request, cancellationToken));
        }

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(Direct));
        var storyId = await directionScope.StoryIdAsync(sceneId, cancellationToken);
        await storyScope.LockAsync(storyId, cancellationToken);
        var current = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, cancellationToken);
        Prune(draft, current.References);
        AddWardrobes(draft, current.Story);
        DirectionWriter.Apply(current.Scene, draft, byAuthor: false);
        await CommitAsync(unitOfWork, cancellationToken);
        return new DirectReply { Direction = ToMessage(current) };
    }

    public override async Task<GenerateAudioReply> GenerateAudio(GenerateAudioRequest request, ServerCallContext context)
    {
        var sceneId = StoryIds.Existing(request.SceneId, StoryErrors.SceneNotFound);
        var elementId = request.HasElementId ? StoryIds.Existing(request.ElementId, DirectionErrors.LineNotFound) : (Guid?)null;
        try
        {
            var (scene, storyId) = await audioGenerator.GenerateAsync(sceneId, elementId, request.Regenerate, context.CancellationToken);
            using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(GenerateAudio));
            var story = await StoryAsync(storyId, context.CancellationToken);
            return new GenerateAudioReply { Direction = DirectionMessages.ToMessage(scene, audioMessages.For(scene, story)) };
        }
        catch (SpeechException e)
        {
            logger.LogWarning(e, "A line could not be spoken");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message));
        }
    }

    public override async Task<ListVoicesReply> ListVoices(ListVoicesRequest request, ServerCallContext context)
    {
        try
        {
            var reply = new ListVoicesReply { Provider = speech.Provider };
            reply.Voices.AddRange((await speech.ListVoicesAsync(context.CancellationToken))
                .Select(v => new Voice { Id = v.Id, Name = v.Name }));
            return reply;
        }
        catch (SpeechException e)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message));
        }
    }

    private SceneDirection ToMessage(DirectedScene directed) =>
        DirectionMessages.ToMessage(directed.Scene, audioMessages.For(directed.Scene, directed.Story));

    public override async Task<DescribeReply> Describe(DescribeRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        switch (request.SubjectCase)
        {
            case DescribeRequest.SubjectOneofCase.CharacterId:
            {
                var id = StoryIds.Existing(request.CharacterId, CharacterNotFound);
                CharacterRecord subject;
                StoryRecord story;
                List<SceneElementRecord> excerpts;
                using (unitOfWorkFactory.CreateReadOnly(nameof(Describe)))
                {
                    subject = await characterRepository.GetFirstByQueryAsync(
                                  characterQueries.Create().WithId(id).WithWardrobes(), cancellationToken)
                              ?? throw StoryErrors.NotFound(CharacterNotFound);
                    story = await StoryAsync(subject.StoryId, cancellationToken);
                    var spoken = await sceneElementRepository.GetByQueryAsync(
                        sceneElementQueries.Create().InStory(story.Id).SpokenBy(id), cancellationToken);
                    var mentioned = await sceneElementRepository.GetByQueryAsync(
                        sceneElementQueries.Create().InStory(story.Id).MentioningCharacter(id), cancellationToken);
                    excerpts = spoken.Concat(mentioned).DistinctBy(e => e.Id).ToList();
                }

                await AskAsync(() => describer.DescribeAsync(subject, story, excerpts, cancellationToken));

                using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(Describe));
                await storyScope.LockAsync(story.Id, cancellationToken);
                var character = await characterRepository.GetFirstByQueryAsync(
                                    characterQueries.Create().WithId(id).WithWardrobes(), cancellationToken)
                                ?? throw StoryErrors.NotFound(CharacterNotFound);
                character.Description = subject.Description;
                character.Appearance = subject.Appearance;
                character.Voice = subject.Voice;
                StoryLibrary.ApplyWardrobes(character, subject.Wardrobes.OrderBy(w => w.Position).Select(StoryMessages.ToMessage));
                await unitOfWork.CommitAsync(cancellationToken);
                return new DescribeReply { Character = StoryMessages.ToMessage(character) };
            }

            case DescribeRequest.SubjectOneofCase.LocationId:
            {
                var id = StoryIds.Existing(request.LocationId, LocationNotFound);
                LocationRecord subject;
                StoryRecord story;
                List<SceneElementRecord> excerpts;
                using (unitOfWorkFactory.CreateReadOnly(nameof(Describe)))
                {
                    subject = await locationRepository.GetFirstByQueryAsync(locationQueries.Create().WithId(id), cancellationToken)
                              ?? throw StoryErrors.NotFound(LocationNotFound);
                    story = await StoryAsync(subject.StoryId, cancellationToken);
                    var headings = await sceneElementRepository.GetByQueryAsync(
                        sceneElementQueries.Create().InStory(story.Id).AtLocation(id), cancellationToken);
                    var mentioned = await sceneElementRepository.GetByQueryAsync(
                        sceneElementQueries.Create().InStory(story.Id).MentioningLocation(id), cancellationToken);
                    var around = await sceneElementRepository.GetByQueryAsync(
                        sceneElementQueries.Create().InStory(story.Id).OfType(SceneElementTypeRecord.Action), cancellationToken);
                    var scenesThere = headings.Select(h => h.SceneId).ToHashSet();
                    excerpts = headings.Concat(mentioned).Concat(around.Where(a => scenesThere.Contains(a.SceneId)))
                        .DistinctBy(e => e.Id).ToList();
                }

                await AskAsync(() => describer.DescribeAsync(subject, story, excerpts, cancellationToken));

                using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(Describe));
                await storyScope.LockAsync(story.Id, cancellationToken);
                var location = await locationRepository.GetFirstByQueryAsync(locationQueries.Create().WithId(id), cancellationToken)
                               ?? throw StoryErrors.NotFound(LocationNotFound);
                location.Description = subject.Description;
                location.Appearance = subject.Appearance;
                location.Acoustics = subject.Acoustics;
                await unitOfWork.CommitAsync(cancellationToken);
                return new DescribeReply { Location = StoryMessages.ToMessage(location) };
            }

            case DescribeRequest.SubjectOneofCase.PropId:
            {
                var id = StoryIds.Existing(request.PropId, PropNotFound);
                PropRecord subject;
                StoryRecord story;
                IReadOnlyList<SceneElementRecord> excerpts;
                using (unitOfWorkFactory.CreateReadOnly(nameof(Describe)))
                {
                    subject = await propRepository.GetFirstByQueryAsync(propQueries.Create().WithId(id), cancellationToken)
                              ?? throw StoryErrors.NotFound(PropNotFound);
                    story = await StoryAsync(subject.StoryId, cancellationToken);
                    excerpts = await sceneElementRepository.GetByQueryAsync(
                        sceneElementQueries.Create().InStory(story.Id).MentioningProp(id), cancellationToken);
                }

                await AskAsync(() => describer.DescribeAsync(subject, story, excerpts, cancellationToken));

                using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(Describe));
                await storyScope.LockAsync(story.Id, cancellationToken);
                var prop = await propRepository.GetFirstByQueryAsync(propQueries.Create().WithId(id), cancellationToken)
                           ?? throw StoryErrors.NotFound(PropNotFound);
                prop.Description = subject.Description;
                await unitOfWork.CommitAsync(cancellationToken);
                return new DescribeReply { Prop = StoryMessages.ToMessage(prop) };
            }

            default:
                throw StoryErrors.Invalid("Say what to describe.");
        }
    }

    private async Task<StoryRecord> StoryAsync(Guid storyId, CancellationToken cancellationToken)
    {
        return await storyRepository.GetFirstByQueryAsync(
                   storyQueries.Create().WithId(storyId).WithLibrary().WithOutline(), cancellationToken)
               ?? throw StoryErrors.NotFound(StoryErrors.StoryNotFound);
    }

    /// <summary>Model failures reach the author in words, as something to retry, not as a server fault.</summary>
    private async Task<T> AskAsync<T>(Func<Task<T>> ask)
    {
        try
        {
            return await ask();
        }
        catch (LanguageModelUnavailableException e)
        {
            logger.LogWarning(e, "The language model is unavailable");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, DirectionErrors.ModelUnavailable));
        }
        catch (ModelOutputException e)
        {
            logger.LogWarning("The model's answer could not be used: {Reason}", e.Message);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, DirectionErrors.ModelUnusable));
        }
    }

    private Task AskAsync(Func<Task> ask) => AskAsync(async () =>
    {
        await ask();
        return true;
    });

    /// <summary>
    /// Wardrobes a continuity pass named from the scene's text join the character's sheet, so the
    /// look can wear them; a label the sheet gained meanwhile is reused.
    /// </summary>
    private static void AddWardrobes(DirectionDraft draft, StoryRecord story)
    {
        foreach (var look in draft.Appearances.Where(a => a.Wardrobe is not null))
        {
            var character = story.Characters.FirstOrDefault(c => c.Id == look.CharacterId);
            if (character is null)
            {
                look.Wardrobe = null;
                continue;
            }

            var existing = character.Wardrobes.FirstOrDefault(w => LibraryNames.Same(w.Label, look.Wardrobe!.Label));
            if (existing is not null)
            {
                look.Wardrobe = null;
                look.WardrobeId = existing.Id;
                continue;
            }

            look.Wardrobe!.Position = character.Wardrobes.Count == 0 ? 0 : character.Wardrobes.Max(w => w.Position) + 1;
            character.Wardrobes.Add(look.Wardrobe);
        }
    }

    /// <summary>The scene may have changed while the model worked: rows pointing at what is gone are dropped.</summary>
    private static void Prune(DirectionDraft draft, DirectionReferences references)
    {
        draft.Cues.RemoveAll(c => !references.Elements.ContainsKey(c.ElementId));
        foreach (var cue in draft.Cues.Where(c => c.NarratorId is { } id && !references.NarratorIds.Contains(id)))
        {
            cue.NarratorId = null;
        }

        draft.Panels.RemoveAll(p => !references.Elements.ContainsKey(p.FirstElementId) || !references.Elements.ContainsKey(p.LastElementId));
        foreach (var panel in draft.Panels)
        {
            panel.Placements.RemoveAll(p => !references.Story.CharacterIds.Contains(p.CharacterId));
            foreach (var placement in panel.Placements)
            {
                if (placement.WardrobeId is { } wardrobeId
                    && !(references.WardrobesByCharacter.TryGetValue(placement.CharacterId, out var wardrobes) && wardrobes.Contains(wardrobeId)))
                {
                    placement.WardrobeId = null;
                }
            }

            panel.Props.RemoveAll(p => !references.Story.PropIds.Contains(p.PropId));
            if (panel.LocationId is { } locationId && !references.Story.LocationIds.Contains(locationId))
            {
                panel.LocationId = null;
            }
        }

        draft.Appearances.RemoveAll(a => !references.Story.CharacterIds.Contains(a.CharacterId));
        foreach (var look in draft.Appearances)
        {
            if (look.Wardrobe is null
                && look.WardrobeId is { } wardrobeId
                && !(references.WardrobesByCharacter.TryGetValue(look.CharacterId, out var wardrobes) && wardrobes.Contains(wardrobeId)))
            {
                look.WardrobeId = null;
            }

            if (look.ChangeElementId is { } element && !references.Elements.ContainsKey(element))
            {
                look.ChangeElementId = null;
            }
        }

        draft.PropStates.RemoveAll(p => !references.Story.PropIds.Contains(p.PropId));
        foreach (var state in draft.PropStates)
        {
            if (state.ChangeElementId is { } element && !references.Elements.ContainsKey(element))
            {
                state.ChangeElementId = null;
            }
        }

        draft.LocationStates.RemoveAll(l => !references.Story.LocationIds.Contains(l.LocationId));

        var position = 0;
        foreach (var panel in draft.Panels.OrderBy(p => references.Elements[p.FirstElementId].Position))
        {
            panel.Position = position++;
        }
    }

    private static async Task CommitAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (StoryErrors.IsUniqueViolation(exception))
        {
            throw StoryErrors.Invalid("Another scene already uses one of these rows.");
        }
    }
}
