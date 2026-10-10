using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Api.Services.Imaging;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Api.Services.Speech;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Screenplay.Models;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using ExtraRecord = ProtoFast.Data.ThePlot.Entities.Extra;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;
using VoiceProfileRecord = ProtoFast.Data.ThePlot.Entities.VoiceProfile;

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
    VoiceCaster caster,
    CueAudioGenerator audioGenerator,
    CueAudioMessages audioMessages,
    FrameImageGenerator frameImages,
    LookImageGenerator lookImages,
    LibraryImageGenerator libraryImages,
    ImageMessages imageMessages,
    ISpeechSynthesizer speech,
    IStoryRepository storyRepository,
    IQueryFactory<StoryRecord, IStoryQuery> storyQueries,
    ICharacterRepository characterRepository,
    IQueryFactory<CharacterRecord, ICharacterQuery> characterQueries,
    ILocationRepository locationRepository,
    IQueryFactory<LocationRecord, ILocationQuery> locationQueries,
    IPropRepository propRepository,
    IQueryFactory<PropRecord, IPropQuery> propQueries,
    IExtraRepository extraRepository,
    IQueryFactory<ExtraRecord, IExtraQuery> extraQueries,
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
        var added = await AddExtrasAsync(draft, current.Story, cancellationToken);
        Prune(draft, current.References, added);
        AddWardrobes(draft, current.Story);
        AddLocationVariations(draft, current.Story);
        DirectionWriter.Apply(current.Scene, draft, byAuthor: false);
        await CommitAsync(unitOfWork, cancellationToken);
        var reply = new DirectReply { Direction = ToMessage(current) };
        reply.Extras.AddRange(current.Story.Extras.OrderBy(e => e.Name).Select(StoryMessages.ToMessage));

        // A frame the pass made has its id only once saved; its first row still names it.
        reply.Findings.AddRange(draft.Findings
            .Select(f => (Finding: f, Stored: current.Scene.Frames.FirstOrDefault(s => s.FirstElementId == f.Frame.FirstElementId)))
            .Where(f => f.Stored is not null)
            .Select(f => new FrameFinding { FrameId = f.Stored!.Id.ToString(), Check = f.Finding.Check, Issue = f.Finding.Issue }));
        return reply;
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
            return new GenerateAudioReply { Direction = ToMessage(new DirectedScene(scene, story, [], DirectionScope.References(scene, story))) };
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

    public override async Task<CastVoiceReply> CastVoice(CastVoiceRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var id = StoryIds.Existing(request.CharacterId, CharacterNotFound);
        CharacterRecord subject;
        StoryRecord story;
        using (unitOfWorkFactory.CreateReadOnly(nameof(CastVoice)))
        {
            subject = await characterRepository.GetFirstByQueryAsync(characterQueries.Create().WithId(id), cancellationToken)
                      ?? throw StoryErrors.NotFound(CharacterNotFound);
            story = await StoryAsync(subject.StoryId, cancellationToken);
        }

        VoiceProfileRecord voice;
        try
        {
            voice = await AskAsync(() => caster.CastAsync(subject, story, cancellationToken))
                    ?? throw new RpcException(new Status(StatusCode.FailedPrecondition, $"No other library voice fits {subject.Name}."));
        }
        catch (SpeechException e)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message));
        }

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CastVoice));
        await storyScope.LockAsync(story.Id, cancellationToken);
        var character = await characterRepository.GetFirstByQueryAsync(
                            characterQueries.Create().WithId(id).WithWardrobes(), cancellationToken)
                        ?? throw StoryErrors.NotFound(CharacterNotFound);
        character.Voice = new VoiceProfileRecord
        {
            Description = voice.Description,
            Baseline = voice.Baseline,
            VoiceId = voice.VoiceId,
            VoiceName = voice.VoiceName,
            PreviewUrl = voice.PreviewUrl,
        };
        await unitOfWork.CommitAsync(cancellationToken);
        return new CastVoiceReply { Character = StoryMessages.ToMessage(character) };
    }

    public override async Task<ListVoicesReply> SearchVoices(SearchVoicesRequest request, ServerCallContext context)
    {
        try
        {
            var query = new VoiceQuery(
                string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim(),
                request.Search, request.Gender, request.Age, request.Accent, request.Narration);
            var reply = new ListVoicesReply { Provider = speech.Provider };
            reply.Voices.AddRange((await speech.SearchVoicesAsync(query, context.CancellationToken))
                .Select(v => new Voice { Id = v.Id, Name = v.Name, Description = v.Description, Traits = v.Traits, PreviewUrl = v.PreviewUrl }));
            return reply;
        }
        catch (SpeechException e)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message));
        }
    }

    private SceneDirection ToMessage(DirectedScene directed) =>
        DirectionMessages.ToMessage(
            directed.Scene,
            audioMessages.For(directed.Scene, directed.Story),
            imageMessages.ForFrames(directed),
            imageMessages.ForLooks(directed.Story));

    public override async Task<GenerateFrameImageReply> GenerateFrameImage(GenerateFrameImageRequest request, ServerCallContext context)
    {
        var sceneId = StoryIds.Existing(request.SceneId, StoryErrors.SceneNotFound);
        var frameId = StoryIds.Existing(request.FrameId, DirectionErrors.FrameNotFound);
        try
        {
            var directed = request.Refine.Length > 0
                ? await frameImages.RefineAsync(sceneId, frameId, request.Refine, RefineReference(request.RefineReferenceAssetId), context.CancellationToken)
                : await frameImages.GenerateAsync(sceneId, frameId, request.Regenerate, context.CancellationToken);
            return new GenerateFrameImageReply { Direction = ToMessage(directed) };
        }
        catch (ImageException e)
        {
            logger.LogWarning(e, "A frame could not be pictured");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message));
        }
    }

    public override async Task<GenerateLookImageReply> GenerateLookImage(GenerateLookImageRequest request, ServerCallContext context)
    {
        var sceneId = StoryIds.Existing(request.SceneId, StoryErrors.SceneNotFound);
        var characterId = StoryIds.Existing(request.CharacterId, CharacterNotFound);
        var elementId = request.HasElementId ? StoryIds.Existing(request.ElementId, DirectionErrors.LineNotFound) : (Guid?)null;
        try
        {
            var directed = request.Refine.Length > 0
                ? await lookImages.RefineAsync(
                    sceneId, characterId, elementId, request.Refine, RefineReference(request.RefineReferenceAssetId), context.CancellationToken)
                : await lookImages.GenerateAsync(sceneId, characterId, elementId, request.Regenerate, context.CancellationToken);
            return new GenerateLookImageReply { Direction = ToMessage(directed) };
        }
        catch (ImageException e)
        {
            logger.LogWarning(e, "A look could not be pictured");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message));
        }
    }

    public override async Task<GenerateLibraryImageReply> GenerateLibraryImage(GenerateLibraryImageRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var (source, id, storyId) = request.SubjectCase switch
        {
            GenerateLibraryImageRequest.SubjectOneofCase.CharacterId => await SubjectAsync(
                ReferenceSource.Character, StoryIds.Existing(request.CharacterId, CharacterNotFound), CharacterNotFound,
                (id, ct) => characterRepository.GetFirstByQueryAsync(characterQueries.Create().WithId(id), c => (Guid?)c.StoryId, ct),
                cancellationToken),
            GenerateLibraryImageRequest.SubjectOneofCase.LocationId => await SubjectAsync(
                ReferenceSource.Location, StoryIds.Existing(request.LocationId, LocationNotFound), LocationNotFound,
                (id, ct) => locationRepository.GetFirstByQueryAsync(locationQueries.Create().WithId(id), l => (Guid?)l.StoryId, ct),
                cancellationToken),
            GenerateLibraryImageRequest.SubjectOneofCase.LocationVariationId => await SubjectAsync(
                ReferenceSource.LocationVariation, StoryIds.Existing(request.LocationVariationId, LibraryErrors.LocationVariationNotFound),
                LibraryErrors.LocationVariationNotFound,
                (id, ct) => locationRepository.GetFirstByQueryAsync(locationQueries.Create().WithVariation(id), l => (Guid?)l.StoryId, ct),
                cancellationToken),
            GenerateLibraryImageRequest.SubjectOneofCase.PropId => await SubjectAsync(
                ReferenceSource.Prop, StoryIds.Existing(request.PropId, PropNotFound), PropNotFound,
                (id, ct) => propRepository.GetFirstByQueryAsync(propQueries.Create().WithId(id), p => (Guid?)p.StoryId, ct),
                cancellationToken),
            GenerateLibraryImageRequest.SubjectOneofCase.ExtraId => await SubjectAsync(
                ReferenceSource.Extra, StoryIds.Existing(request.ExtraId, StoryLibrary.ExtraNotFound), StoryLibrary.ExtraNotFound,
                (id, ct) => extraRepository.GetFirstByQueryAsync(extraQueries.Create().WithId(id), e => (Guid?)e.StoryId, ct),
                cancellationToken),
            _ => throw StoryErrors.Invalid("Say which library entry to picture."),
        };

        try
        {
            var story = request.Refine.Length > 0
                ? await libraryImages.RefineAsync(storyId, source, id, request.Refine, RefineReference(request.RefineReferenceAssetId), cancellationToken)
                : await libraryImages.GenerateAsync(storyId, source, id, request.Regenerate, cancellationToken);
            var kind = ReferenceSources.Of(source);
            var image = imageMessages.ForLibrary(story).FirstOrDefault(i => i.SubjectKind == kind && i.SubjectId == id.ToString())
                        ?? new LibraryImage { SubjectKind = kind, SubjectId = id.ToString() };
            return new GenerateLibraryImageReply { Image = image };
        }
        catch (ImageException e)
        {
            logger.LogWarning(e, "A library entry could not be pictured");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message));
        }
    }

    public override async Task<ListLibraryImagesReply> ListLibraryImages(ListLibraryImagesRequest request, ServerCallContext context)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListLibraryImages));
        var story = await StoryAsync(storyId, context.CancellationToken);
        var reply = new ListLibraryImagesReply();
        reply.Images.AddRange(imageMessages.ForLibrary(story));
        return reply;
    }

    private static Guid? RefineReference(string assetId) =>
        assetId.Length > 0 ? StoryIds.Existing(assetId, ImageRefiner.ReferenceNotFound) : null;

    /// <summary>The library entry's story, read as its owner; anyone else's entry reads as not found.</summary>
    private async Task<(ReferenceSource Source, Guid Id, Guid StoryId)> SubjectAsync(
        ReferenceSource source, Guid id, string notFound, Func<Guid, CancellationToken, Task<Guid?>> storyOf, CancellationToken ct)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(SubjectAsync));
        return (source, id, await storyOf(id, ct) ?? throw StoryErrors.NotFound(notFound));
    }

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
                if (string.IsNullOrEmpty(subject.Voice?.VoiceId))
                {
                    // The sheet is worth keeping when the library can't be reached; casting can be retried on its own.
                    try
                    {
                        subject.Voice = await AskAsync(() => caster.CastAsync(subject, story, cancellationToken)) ?? subject.Voice;
                    }
                    catch (SpeechException e)
                    {
                        logger.LogWarning(e, "No voice was cast for {Character}", id);
                    }
                }

                using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(Describe));
                await storyScope.LockAsync(story.Id, cancellationToken);
                var character = await characterRepository.GetFirstByQueryAsync(
                                    characterQueries.Create().WithId(id).WithWardrobes(), cancellationToken)
                                ?? throw StoryErrors.NotFound(CharacterNotFound);
                character.Kind = subject.Kind;
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
                var location = await locationRepository.GetFirstByQueryAsync(locationQueries.Create().WithId(id).WithVariations(), cancellationToken)
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

            case DescribeRequest.SubjectOneofCase.ExtraId:
            {
                var id = StoryIds.Existing(request.ExtraId, StoryLibrary.ExtraNotFound);
                ExtraRecord subject;
                StoryRecord story;
                IReadOnlyList<SceneElementRecord> excerpts;
                using (unitOfWorkFactory.CreateReadOnly(nameof(Describe)))
                {
                    subject = await extraRepository.GetFirstByQueryAsync(extraQueries.Create().WithId(id), cancellationToken)
                              ?? throw StoryErrors.NotFound(StoryLibrary.ExtraNotFound);
                    story = await StoryAsync(subject.StoryId, cancellationToken);
                    excerpts = await sceneElementRepository.GetByQueryAsync(
                        sceneElementQueries.Create().InStory(story.Id).InScenesWithExtra(id).InOrder(), cancellationToken);
                }

                await AskAsync(() => describer.DescribeAsync(subject, story, excerpts, cancellationToken));

                using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(Describe));
                await storyScope.LockAsync(story.Id, cancellationToken);
                var extra = await extraRepository.GetFirstByQueryAsync(extraQueries.Create().WithId(id), cancellationToken)
                            ?? throw StoryErrors.NotFound(StoryLibrary.ExtraNotFound);
                extra.Description = subject.Description;
                extra.Wardrobe = subject.Wardrobe;
                extra.Headcount = subject.Headcount;
                await unitOfWork.CommitAsync(cancellationToken);
                return new DescribeReply { Extra = StoryMessages.ToMessage(extra) };
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

        // A change only carries a wardrobe the opening look added, which is on the sheet by now.
        foreach (var change in draft.LookChanges.Where(c => c.Wardrobe is not null))
        {
            var character = story.Characters.FirstOrDefault(c => c.Id == change.CharacterId);
            var added = character?.Wardrobes.FirstOrDefault(w => ReferenceEquals(w, change.Wardrobe) || LibraryNames.Same(w.Label, change.Wardrobe!.Label));
            change.Wardrobe = added is { Id: var id } && id == Guid.Empty ? added : null;
            change.WardrobeId = added is null || added.Id == Guid.Empty ? null : added.Id;
        }
    }

    /// <summary>
    /// Parts of places the pass described join their location, so frames can stand in them; a part of
    /// the same name added meanwhile is used instead, and a part of a location now gone is dropped.
    /// </summary>
    private static void AddLocationVariations(DirectionDraft draft, StoryRecord story)
    {
        foreach (var frame in draft.Frames.Where(f => f.LocationVariation is not null))
        {
            var variation = frame.LocationVariation!;
            frame.LocationVariation = null;
            var location = story.Locations.FirstOrDefault(l => l.Id == variation.LocationId);
            if (location is null)
            {
                continue;
            }

            if (location.Variations.FirstOrDefault(v => ReferenceEquals(v, variation) || LibraryNames.Same(v.Name, variation.Name)) is { } existing)
            {
                frame.LocationVariation = existing.Id == Guid.Empty ? existing : null;
                frame.LocationVariationId = existing.Id == Guid.Empty ? null : existing.Id;
                continue;
            }

            variation.Position = location.Variations.Count == 0 ? 0 : location.Variations.Max(v => v.Position) + 1;
            location.Variations.Add(variation);
            frame.LocationVariation = variation;
        }
    }

    /// <summary>
    /// Extras the pass found the library lacks join it, unless one of the same name was added
    /// meanwhile, which the rows then point at instead.
    /// </summary>
    /// <returns>The ids of the extras added.</returns>
    private async Task<IReadOnlySet<Guid>> AddExtrasAsync(DirectionDraft draft, StoryRecord story, CancellationToken cancellationToken)
    {
        HashSet<Guid> added = [];
        foreach (var extra in draft.NewExtras)
        {
            var id = extra.Id;
            if (story.Extras.FirstOrDefault(e => LibraryNames.Same(e.Name, extra.Name)) is { } existing)
            {
                id = existing.Id;
            }
            else
            {
                extra.StoryId = story.Id;
                await extraRepository.AddAsync(extra, cancellationToken);
                if (!story.Extras.Contains(extra))
                {
                    story.Extras.Add(extra);
                }

                added.Add(id);
            }

            foreach (var row in draft.Extras.Where(e => e.ExtraId == extra.Id))
            {
                row.ExtraId = id;
            }

            foreach (var row in draft.Frames.SelectMany(f => f.Extras).Where(e => e.ExtraId == extra.Id))
            {
                row.ExtraId = id;
            }
        }

        draft.Extras = draft.Extras.DistinctBy(e => e.ExtraId).ToList();
        foreach (var frame in draft.Frames)
        {
            frame.Extras = frame.Extras.DistinctBy(e => e.ExtraId).ToList();
        }

        return added;
    }

    /// <summary>The scene may have changed while the model worked: rows pointing at what is gone are dropped.</summary>
    /// <param name="addedExtras">Extras this write adds to the library.</param>
    private static void Prune(DirectionDraft draft, DirectionReferences references, IReadOnlySet<Guid> addedExtras)
    {
        bool Extra(Guid id) => references.ExtraIds.Contains(id) || addedExtras.Contains(id);
        draft.Extras.RemoveAll(e => !Extra(e.ExtraId));
        draft.Cues.RemoveAll(c => !references.Elements.ContainsKey(c.ElementId));
        foreach (var cue in draft.Cues.Where(c => c.NarratorId is { } id && !references.NarratorIds.Contains(id)))
        {
            cue.NarratorId = null;
        }

        draft.Frames.RemoveAll(p => !references.Elements.ContainsKey(p.FirstElementId) || !references.Elements.ContainsKey(p.LastElementId));
        foreach (var frame in draft.Frames)
        {
            frame.Placements.RemoveAll(p => !references.Story.CharacterIds.Contains(p.CharacterId));
            foreach (var placement in frame.Placements)
            {
                if (placement.WardrobeId is { } wardrobeId
                    && !(references.WardrobesByCharacter.TryGetValue(placement.CharacterId, out var wardrobes) && wardrobes.Contains(wardrobeId)))
                {
                    placement.WardrobeId = null;
                }
            }

            frame.Props.RemoveAll(p => !references.Story.PropIds.Contains(p.PropId));
            frame.Extras.RemoveAll(e => !Extra(e.ExtraId));
            if (frame.LocationId is { } locationId && !references.Story.LocationIds.Contains(locationId))
            {
                frame.LocationId = null;
            }

            if (frame.LocationVariationId is { } variationId && !references.LocationVariationIds.Contains(variationId))
            {
                frame.LocationVariationId = null;
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

        foreach (var look in draft.Appearances)
        {
            look.Holding.RemoveAll(id => !references.Story.PropIds.Contains(id));
        }

        draft.LookChanges.RemoveAll(c => !references.Story.CharacterIds.Contains(c.CharacterId) || !references.Elements.ContainsKey(c.ElementId));
        foreach (var change in draft.LookChanges)
        {
            if (change.Wardrobe is null
                && change.WardrobeId is { } wardrobeId
                && !(references.WardrobesByCharacter.TryGetValue(change.CharacterId, out var wardrobes) && wardrobes.Contains(wardrobeId)))
            {
                change.WardrobeId = null;
            }

            change.Holding.RemoveAll(id => !references.Story.PropIds.Contains(id));
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
        foreach (var frame in draft.Frames.OrderBy(p => references.Elements[p.FirstElementId].Position))
        {
            frame.Position = position++;
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
