using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;

namespace ProtoFast.Api.Services.Screenplays;

/// <summary>
/// A story's characters, locations, props and vocabulary. Names are unique per kind ignoring case,
/// which the case-sensitive indexes cannot enforce, so each write checks them under the story lock.
/// </summary>
public sealed class StoryLibrary(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    ICharacterRepository characterRepository,
    IQueryFactory<CharacterRecord, ICharacterQuery> characterQueries,
    ILocationRepository locationRepository,
    IQueryFactory<LocationRecord, ILocationQuery> locationQueries,
    IPropRepository propRepository,
    IQueryFactory<PropRecord, IPropQuery> propQueries,
    ISceneElementRepository sceneElementRepository,
    IQueryFactory<SceneElementRecord, ISceneElementQuery> sceneElementQueries)
{
    private const string CharacterNotFound = "That character is no longer in the library.";
    private const string LocationNotFound = "That location is no longer in the library.";
    private const string PropNotFound = "That prop is no longer in the library.";

    public async Task<Character> CreateCharacterAsync(CreateCharacterRequest request, CancellationToken cancellationToken)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateCharacterAsync));
        await storyScope.LockAsync(storyId, cancellationToken);

        var story = await storyScope.GetAsync(storyId, cancellationToken);
        var characters = await characterRepository.GetByQueryAsync(
            characterQueries.Create().InStory(storyId), cancellationToken);
        var character = new CharacterRecord
        {
            StoryId = storyId,
            Name = LibraryNames.Check(characters.Select(c => (c.Id, c.Name)), request.Name, "character"),
            Kind = VocabularyLabels.Pick(
                request.Kind, VocabularyLabels.CharacterKinds(story.Vocabulary), current: null, "character kind")!,
            Hue = LibraryNames.Hue(request.Hue),
        };

        await characterRepository.AddAsync(character, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return StoryMessages.ToMessage(character);
    }

    public async Task UpdateCharacterAsync(UpdateCharacterRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.CharacterId, CharacterNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateCharacterAsync));
        var storyId = await characterRepository.GetFirstByQueryAsync(
                          characterQueries.Create().WithId(id), c => (Guid?)c.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(CharacterNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var story = await storyScope.GetAsync(storyId, cancellationToken);
        var characters = await characterRepository.GetByQueryAsync(
            characterQueries.Create().InStory(storyId), cancellationToken);
        var character = characters.Single(c => c.Id == id);
        var name = LibraryNames.Check(characters.Select(c => (c.Id, c.Name)), request.Name, "character", id);
        character.Kind = VocabularyLabels.Pick(
            request.Kind, VocabularyLabels.CharacterKinds(story.Vocabulary), character.Kind, "character kind")!;
        character.Hue = LibraryNames.Hue(request.Hue);
        if (name != character.Name)
        {
            await RenameMentionsAsync(
                sceneElementQueries.Create().InStory(storyId).MentioningCharacter(id),
                m => m.CharacterId == id,
                name,
                cancellationToken);
            character.Name = name;
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteCharacterAsync(DeleteCharacterRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.CharacterId, CharacterNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteCharacterAsync));
        var storyId = await characterRepository.GetFirstByQueryAsync(
                          characterQueries.Create().WithId(id), c => (Guid?)c.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(CharacterNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        // The database drops the mentions and clears the speaker; the "@Name" text stays.
        await characterRepository.DeleteByQueryAsync(characterQueries.Create().WithId(id), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task<Location> CreateLocationAsync(CreateLocationRequest request, CancellationToken cancellationToken)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateLocationAsync));
        await storyScope.LockAsync(storyId, cancellationToken);

        var locations = await locationRepository.GetByQueryAsync(
            locationQueries.Create().InStory(storyId), cancellationToken);
        var location = new LocationRecord
        {
            StoryId = storyId,
            Name = LibraryNames.Check(locations.Select(l => (l.Id, l.Name)), request.Name, "location"),
            Setting = StoryMessages.FromMessage(request.Setting),
            Hue = LibraryNames.Hue(request.Hue),
        };

        await locationRepository.AddAsync(location, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return StoryMessages.ToMessage(location);
    }

    public async Task UpdateLocationAsync(UpdateLocationRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.LocationId, LocationNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateLocationAsync));
        var storyId = await locationRepository.GetFirstByQueryAsync(
                          locationQueries.Create().WithId(id), l => (Guid?)l.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(LocationNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var locations = await locationRepository.GetByQueryAsync(
            locationQueries.Create().InStory(storyId), cancellationToken);
        var location = locations.Single(l => l.Id == id);
        var name = LibraryNames.Check(locations.Select(l => (l.Id, l.Name)), request.Name, "location", id);
        location.Setting = StoryMessages.FromMessage(request.Setting);
        location.Hue = LibraryNames.Hue(request.Hue);
        if (name != location.Name)
        {
            await RenameMentionsAsync(
                sceneElementQueries.Create().InStory(storyId).MentioningLocation(id),
                m => m.LocationId == id,
                name,
                cancellationToken);
            location.Name = name;
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteLocationAsync(DeleteLocationRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.LocationId, LocationNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteLocationAsync));
        var storyId = await locationRepository.GetFirstByQueryAsync(
                          locationQueries.Create().WithId(id), l => (Guid?)l.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(LocationNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        // The database drops the mentions and clears the headings; the "@Name" text stays.
        await locationRepository.DeleteByQueryAsync(locationQueries.Create().WithId(id), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task<Prop> CreatePropAsync(CreatePropRequest request, CancellationToken cancellationToken)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreatePropAsync));
        await storyScope.LockAsync(storyId, cancellationToken);

        var props = await propRepository.GetByQueryAsync(propQueries.Create().InStory(storyId), cancellationToken);
        var prop = new PropRecord
        {
            StoryId = storyId,
            Name = LibraryNames.Check(props.Select(p => (p.Id, p.Name)), request.Name, "prop"),
        };

        await propRepository.AddAsync(prop, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return StoryMessages.ToMessage(prop);
    }

    public async Task UpdatePropAsync(UpdatePropRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.PropId, PropNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdatePropAsync));
        var storyId = await propRepository.GetFirstByQueryAsync(
                          propQueries.Create().WithId(id), p => (Guid?)p.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(PropNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var props = await propRepository.GetByQueryAsync(propQueries.Create().InStory(storyId), cancellationToken);
        var prop = props.Single(p => p.Id == id);
        var name = LibraryNames.Check(props.Select(p => (p.Id, p.Name)), request.Name, "prop", id);
        if (name != prop.Name)
        {
            await RenameMentionsAsync(
                sceneElementQueries.Create().InStory(storyId).MentioningProp(id),
                m => m.PropId == id,
                name,
                cancellationToken);
            prop.Name = name;
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeletePropAsync(DeletePropRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.PropId, PropNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeletePropAsync));
        var storyId = await propRepository.GetFirstByQueryAsync(
                          propQueries.Create().WithId(id), p => (Guid?)p.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(PropNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        await propRepository.DeleteByQueryAsync(propQueries.Create().WithId(id), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task SaveVocabularyAsync(SaveVocabularyRequest request, CancellationToken cancellationToken)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        var vocabulary = VocabularyLabels.Validate(request.Vocabulary ?? new StoryVocabulary());
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(SaveVocabularyAsync));
        await storyScope.LockAsync(storyId, cancellationToken);

        var story = await storyScope.GetAsync(storyId, cancellationToken);
        story.Vocabulary = vocabulary;
        await unitOfWork.CommitAsync(cancellationToken);
    }

    private async Task RenameMentionsAsync(
        ISceneElementQuery elements,
        Func<SceneElementMentionRecord, bool> isTarget,
        string name,
        CancellationToken cancellationToken)
    {
        foreach (var element in await sceneElementRepository.GetByQueryAsync(elements.WithMentions(), cancellationToken))
        {
            MentionRewriter.Rename(element, isTarget, name);
        }
    }
}
