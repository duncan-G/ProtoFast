using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Screenplays;

public sealed class StoryScope(
    IStoryRepository storyRepository,
    IQueryFactory<StoryRecord, IStoryQuery> storyQueries,
    ICharacterRepository characterRepository,
    IQueryFactory<CharacterRecord, ICharacterQuery> characterQueries,
    ILocationRepository locationRepository,
    IQueryFactory<LocationRecord, ILocationQuery> locationQueries,
    IPropRepository propRepository,
    IQueryFactory<PropRecord, IPropQuery> propQueries)
{
    /// <summary>
    /// Marks the story modified. The row stays locked until the write commits, so writes to one
    /// story (name checks, rename rewrites, scene saves) take turns. Call it before reading.
    /// </summary>
    public async Task LockAsync(Guid storyId, CancellationToken cancellationToken)
    {
        var touched = await storyRepository.UpdateByQueryAsync(
            storyQueries.Create().WithId(storyId),
            s => s.SetProperty(story => story.DateLastModified, DateTime.UtcNow),
            cancellationToken);

        if (touched == 0)
        {
            throw StoryErrors.NotFound(StoryErrors.StoryNotFound);
        }
    }

    public async Task<StoryRecord> GetAsync(Guid storyId, CancellationToken cancellationToken) =>
        await storyRepository.GetFirstByQueryAsync(storyQueries.Create().WithId(storyId), cancellationToken)
        ?? throw StoryErrors.NotFound(StoryErrors.StoryNotFound);

    public async Task<StoryReferences> ReferencesAsync(Guid storyId, CancellationToken cancellationToken)
    {
        var story = await GetAsync(storyId, cancellationToken);
        var characterIds = await characterRepository.GetByQueryAsync(
            characterQueries.Create().InStory(storyId), c => c.Id, cancellationToken);
        var locationIds = await locationRepository.GetByQueryAsync(
            locationQueries.Create().InStory(storyId), l => l.Id, cancellationToken);
        var propIds = await propRepository.GetByQueryAsync(
            propQueries.Create().InStory(storyId), p => p.Id, cancellationToken);

        return new StoryReferences
        {
            CharacterIds = characterIds.ToHashSet(),
            LocationIds = locationIds.ToHashSet(),
            PropIds = propIds.ToHashSet(),
            TimesOfDay = VocabularyLabels.TimesOfDay(story.Vocabulary),
            Transitions = VocabularyLabels.Transitions(story.Vocabulary),
        };
    }
}
