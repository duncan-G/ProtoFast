using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Directing;

/// <summary>Loads what the director layer is read and written against.</summary>
public sealed class DirectionScope(
    ISceneRepository sceneRepository,
    IQueryFactory<SceneRecord, ISceneQuery> sceneQueries,
    IStoryRepository storyRepository,
    IQueryFactory<StoryRecord, IStoryQuery> storyQueries)
{
    public async Task<Guid> StoryIdAsync(Guid sceneId, CancellationToken cancellationToken) =>
        await sceneRepository.GetFirstByQueryAsync(
            sceneQueries.Create().WithId(sceneId), s => (Guid?)s.Container.StoryId, cancellationToken)
        ?? throw StoryErrors.NotFound(StoryErrors.SceneNotFound);

    /// <param name="withEarlierScenes">Also load the scenes before this one, for continuity and story days.</param>
    public async Task<DirectedScene> LoadAsync(Guid sceneId, bool withEarlierScenes, CancellationToken cancellationToken)
    {
        var storyId = await StoryIdAsync(sceneId, cancellationToken);
        var scene = await sceneRepository.GetFirstByQueryAsync(
                        sceneQueries.Create().WithId(sceneId).WithElements().WithDirection(), cancellationToken)
                    ?? throw StoryErrors.NotFound(StoryErrors.SceneNotFound);
        return await LoadAsync(scene, storyId, withEarlierScenes, cancellationToken);
    }

    /// <param name="scene">Already loaded with its elements and director layer.</param>
    public async Task<DirectedScene> LoadAsync(
        SceneRecord scene, Guid storyId, bool withEarlierScenes, CancellationToken cancellationToken)
    {
        var story = await storyRepository.GetFirstByQueryAsync(
                        storyQueries.Create().WithId(storyId).WithLibrary().WithOutline(), cancellationToken)
                    ?? throw StoryErrors.NotFound(StoryErrors.StoryNotFound);

        IReadOnlyList<SceneRecord> earlier = [];
        if (withEarlierScenes)
        {
            var all = await sceneRepository.GetByQueryAsync(
                sceneQueries.Create().InStory(storyId).InOrder().WithElements().WithDirection(), cancellationToken);
            earlier = all.TakeWhile(s => s.Id != scene.Id).ToList();
        }

        return new DirectedScene(scene, story, earlier, References(scene, story));
    }

    public static DirectionReferences References(SceneRecord scene, StoryRecord story) => new()
    {
        Elements = scene.Elements.ToDictionary(e => e.Id),
        Story = new StoryReferences
        {
            CharacterIds = story.Characters.Select(c => c.Id).ToHashSet(),
            LocationIds = story.Locations.Select(l => l.Id).ToHashSet(),
            PropIds = story.Props.Select(p => p.Id).ToHashSet(),
            TimesOfDay = VocabularyLabels.TimesOfDay(story.Vocabulary),
            Transitions = VocabularyLabels.Transitions(story.Vocabulary),
            Extensions = VocabularyLabels.Extensions(story.Vocabulary),
        },
        WardrobesByCharacter = story.Characters.ToDictionary(
            c => c.Id, c => (IReadOnlySet<Guid>)c.Wardrobes.Select(w => w.Id).ToHashSet()),
        NarratorIds = story.Narrators.Select(n => n.Id).ToHashSet(),
        ExtraIds = story.Extras.Select(e => e.Id).ToHashSet(),
        DeliveryTags = VocabularyLabels.DeliveryTags(story.Vocabulary),
    };
}
