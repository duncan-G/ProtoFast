using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using CharacterWardrobeRecord = ProtoFast.Data.ThePlot.Entities.CharacterWardrobe;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using LocationVariationRecord = ProtoFast.Data.ThePlot.Entities.LocationVariation;
using NarratorRecord = ProtoFast.Data.ThePlot.Entities.Narrator;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using ExtraRecord = ProtoFast.Data.ThePlot.Entities.Extra;
using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Screenplays;

/// <summary>
/// A story's characters, locations, props, extras and vocabulary. Names are unique per kind ignoring case,
/// which the case-sensitive indexes cannot enforce, so each write checks them under the story lock.
/// </summary>
public sealed class StoryLibrary(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
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
    IQueryFactory<SceneElementRecord, ISceneElementQuery> sceneElementQueries)
{
    private const string CharacterNotFound = "That character is no longer in the library.";
    private const string LocationNotFound = "That location is no longer in the library.";
    private const string PropNotFound = "That prop is no longer in the library.";
    public const string ExtraNotFound = "Those extras are no longer in the library.";

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

    public async Task<Extra> CreateExtraAsync(CreateExtraRequest request, CancellationToken cancellationToken)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateExtraAsync));
        await storyScope.LockAsync(storyId, cancellationToken);

        var extras = await extraRepository.GetByQueryAsync(extraQueries.Create().InStory(storyId), cancellationToken);
        var extra = new ExtraRecord
        {
            StoryId = storyId,
            Name = LibraryNames.Check(extras.Select(e => (e.Id, e.Name)), request.Name, "extra"),
            Description = SheetText.Description(request.HasDescription ? request.Description : null),
            Wardrobe = SheetText.Description(request.HasWardrobe ? request.Wardrobe : null),
            Headcount = Headcount.Check(request.HasHeadcount, request.Headcount),
        };

        await extraRepository.AddAsync(extra, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return StoryMessages.ToMessage(extra);
    }

    /// <summary>Extras are never mentioned, so a rename touches nothing but the entry.</summary>
    public async Task<Extra> UpdateExtraAsync(UpdateExtraRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.ExtraId, ExtraNotFound);
        var description = SheetText.Description(request.HasDescription ? request.Description : null);
        var wardrobe = SheetText.Description(request.HasWardrobe ? request.Wardrobe : null);
        var headcount = Headcount.Check(request.HasHeadcount, request.Headcount);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateExtraAsync));
        var storyId = await extraRepository.GetFirstByQueryAsync(
                          extraQueries.Create().WithId(id), e => (Guid?)e.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(ExtraNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var extras = await extraRepository.GetByQueryAsync(extraQueries.Create().InStory(storyId), cancellationToken);
        var extra = extras.Single(e => e.Id == id);
        extra.Name = LibraryNames.Check(extras.Select(e => (e.Id, e.Name)), request.Name, "extra", id);
        extra.Description = description;
        extra.Wardrobe = wardrobe;
        extra.Headcount = headcount;

        await unitOfWork.CommitAsync(cancellationToken);
        return StoryMessages.ToMessage(extra);
    }

    public async Task DeleteExtraAsync(DeleteExtraRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.ExtraId, ExtraNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteExtraAsync));
        var storyId = await extraRepository.GetFirstByQueryAsync(
                          extraQueries.Create().WithId(id), e => (Guid?)e.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(ExtraNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        await extraRepository.DeleteByQueryAsync(extraQueries.Create().WithId(id), cancellationToken);
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

    /// <summary>Wardrobes keep their ids when their label survives, so the looks that wear them stay valid.</summary>
    public async Task<Character> UpdateCharacterSheetAsync(UpdateCharacterSheetRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.CharacterId, CharacterNotFound);
        var description = SheetText.Description(request.HasDescription ? request.Description : null);
        var appearance = request.Appearance is { } identity ? StoryMessages.FromMessage(identity) : null;
        var voice = request.Voice is { } profile ? StoryMessages.FromMessage(profile) : null;

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateCharacterSheetAsync));
        var storyId = await characterRepository.GetFirstByQueryAsync(
                          characterQueries.Create().WithId(id), c => (Guid?)c.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(CharacterNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var character = await characterRepository.GetFirstByQueryAsync(
                            characterQueries.Create().WithId(id).WithWardrobes(), cancellationToken)
                        ?? throw StoryErrors.NotFound(CharacterNotFound);
        character.Description = description;
        character.Appearance = appearance;
        character.Voice = voice;
        ApplyWardrobes(character, request.Wardrobes);

        await unitOfWork.CommitAsync(cancellationToken);
        return StoryMessages.ToMessage(character);
    }

    public async Task<Location> UpdateLocationSheetAsync(UpdateLocationSheetRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.LocationId, LocationNotFound);
        var description = SheetText.Description(request.HasDescription ? request.Description : null);
        var acoustics = request.Acoustics is { } sound ? StoryMessages.FromMessage(sound) : null;

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateLocationSheetAsync));
        var storyId = await locationRepository.GetFirstByQueryAsync(
                          locationQueries.Create().WithId(id), l => (Guid?)l.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(LocationNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var story = await storyScope.GetAsync(storyId, cancellationToken);
        var location = await locationRepository.GetFirstByQueryAsync(
                           locationQueries.Create().WithId(id).WithVariations(), cancellationToken)
                       ?? throw StoryErrors.NotFound(LocationNotFound);
        location.Description = description;
        location.Appearance = request.Appearance is { } look
            ? StoryMessages.FromMessage(look, VocabularyLabels.TimesOfDay(story.Vocabulary))
            : null;
        location.Acoustics = acoustics;

        await unitOfWork.CommitAsync(cancellationToken);
        return StoryMessages.ToMessage(location);
    }

    /// <summary>Variations keep their ids when their name survives, so the frames there stay there.</summary>
    public async Task<Location> SaveLocationVariationsAsync(SaveLocationVariationsRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.LocationId, LocationNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(SaveLocationVariationsAsync));
        var storyId = await locationRepository.GetFirstByQueryAsync(
                          locationQueries.Create().WithId(id), l => (Guid?)l.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(LocationNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var location = await locationRepository.GetFirstByQueryAsync(
                           locationQueries.Create().WithId(id).WithVariations(), cancellationToken)
                       ?? throw StoryErrors.NotFound(LocationNotFound);
        ApplyVariations(location, request.Variations);

        await unitOfWork.CommitAsync(cancellationToken);
        return StoryMessages.ToMessage(location);
    }

    /// <summary>Upserts by id, then by name, as <see cref="ApplyWardrobes"/> does.</summary>
    public static void ApplyVariations(LocationRecord location, IEnumerable<LocationVariation> variations)
    {
        var kept = new List<LocationVariationRecord>();
        var position = 0;
        foreach (var variation in variations)
        {
            var name = SheetText.Field(variation.Name, "variation name");
            if (name.Length == 0)
            {
                throw StoryErrors.Invalid("A location variation needs a name.");
            }

            if (name.Length > LibraryNames.MaxLength)
            {
                throw StoryErrors.Invalid($"Variation names are limited to {LibraryNames.MaxLength} characters.");
            }

            if (kept.Any(v => LibraryNames.Same(v.Name, name)))
            {
                throw StoryErrors.AlreadyExists($"“{name}” is already one of this location’s variations.");
            }

            Guid.TryParse(variation.Id, out var variationId);
            var record = location.Variations.FirstOrDefault(v => variationId != Guid.Empty && v.Id == variationId)
                         ?? location.Variations.FirstOrDefault(v => !kept.Contains(v) && LibraryNames.Same(v.Name, name));
            if (record is null)
            {
                // No id: the database assigns one. A client-picked id would read as an existing row.
                record = new LocationVariationRecord { Name = name };
                location.Variations.Add(record);
            }

            record.Position = position++;
            record.Name = name;
            record.Description = SheetText.Field(variation.Description, "variation description");
            kept.Add(record);
        }

        location.Variations.RemoveAll(v => !kept.Contains(v));
    }

    public async Task<Prop> UpdatePropSheetAsync(UpdatePropSheetRequest request, CancellationToken cancellationToken)
    {
        var id = StoryIds.Existing(request.PropId, PropNotFound);
        var description = SheetText.Description(request.HasDescription ? request.Description : null);

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdatePropSheetAsync));
        var storyId = await propRepository.GetFirstByQueryAsync(
                          propQueries.Create().WithId(id), p => (Guid?)p.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(PropNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var prop = await propRepository.GetFirstByQueryAsync(propQueries.Create().WithId(id), cancellationToken)
                   ?? throw StoryErrors.NotFound(PropNotFound);
        prop.Description = description;

        await unitOfWork.CommitAsync(cancellationToken);
        return StoryMessages.ToMessage(prop);
    }

    /// <summary>Upserts by id; a narrator left out is deleted and its cues fall back to the first.</summary>
    public async Task<IEnumerable<Narrator>> SaveNarratorsAsync(SaveNarratorsRequest request, CancellationToken cancellationToken)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(SaveNarratorsAsync));
        await storyScope.LockAsync(storyId, cancellationToken);

        var story = await storyRepository.GetFirstByQueryAsync(
                        storyQueries.Create().WithId(storyId).WithNarrators(), cancellationToken)
                    ?? throw StoryErrors.NotFound(StoryErrors.StoryNotFound);
        var kept = new List<NarratorRecord>();
        foreach (var (narrator, position) in request.Narrators.Select((n, i) => (n, i)))
        {
            var name = SheetText.Field(narrator.Name, "narrator name");
            if (name.Length == 0)
            {
                throw StoryErrors.Invalid("A narrator needs a name.");
            }

            var voice = StoryMessages.FromMessage(narrator.Voice ?? new VoiceProfile());
            var existing = Guid.TryParse(narrator.Id, out var id) ? story.Narrators.FirstOrDefault(n => n.Id == id) : null;
            if (narrator.Id.Length > 0 && existing is null)
            {
                throw StoryErrors.NotFound("That narrator is no longer in the story.");
            }

            existing ??= new NarratorRecord { Name = name };
            existing.Name = name;
            existing.Position = position;
            existing.Voice = voice;
            kept.Add(existing);
        }

        story.Narrators.RemoveAll(n => !kept.Contains(n));
        story.Narrators.AddRange(kept.Where(n => !story.Narrators.Contains(n)));
        await unitOfWork.CommitAsync(cancellationToken);
        return story.Narrators.OrderBy(n => n.Position).Select(StoryMessages.ToMessage);
    }

    /// <summary>Upserts by id, then by label, so a renamed wardrobe keeps its id and a re-sent one is not duplicated.</summary>
    public static void ApplyWardrobes(CharacterRecord character, IEnumerable<Wardrobe> wardrobes)
    {
        var kept = new List<CharacterWardrobeRecord>();
        var labels = new List<string>();
        var position = 0;
        foreach (var wardrobe in wardrobes)
        {
            var label = SheetText.Field(wardrobe.Label, "wardrobe label");
            if (label.Length == 0)
            {
                throw StoryErrors.Invalid("A wardrobe needs a label.");
            }

            if (label.Length > LibraryNames.MaxLength)
            {
                throw StoryErrors.Invalid($"Wardrobe labels are limited to {LibraryNames.MaxLength} characters.");
            }

            if (labels.Any(l => LibraryNames.Same(l, label)))
            {
                throw StoryErrors.AlreadyExists($"“{label}” is already one of this character’s wardrobes.");
            }

            labels.Add(label);
            Guid.TryParse(wardrobe.Id, out var id);
            var record = character.Wardrobes.FirstOrDefault(w => id != Guid.Empty && w.Id == id)
                         ?? character.Wardrobes.FirstOrDefault(w => !kept.Contains(w) && LibraryNames.Same(w.Label, label));
            if (record is null)
            {
                // No id: the database assigns one. A client-picked id would read as an existing row.
                record = new CharacterWardrobeRecord { Label = label };
                character.Wardrobes.Add(record);
            }

            record.Position = position++;
            record.Label = label;
            record.Description = SheetText.Field(wardrobe.Description, "wardrobe description");
            kept.Add(record);
        }

        character.Wardrobes.RemoveAll(w => !kept.Contains(w));
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
