using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;

namespace ProtoFast.Api.Services.Screenplays;

/// <summary>
/// Writes a scene's title and elements: the body of <c>SaveScene</c>, shared with the director
/// layer's save so a changed line and its cue land in one write. Runs in the caller's unit of work
/// when there is one.
/// </summary>
public sealed class SceneSaver(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    ISceneRepository sceneRepository,
    IQueryFactory<SceneRecord, ISceneQuery> sceneQueries,
    ISceneElementRepository sceneElementRepository)
{
    /// <summary>Elements are upserted by id and any the message leaves out are deleted.</summary>
    /// <param name="includeDirection">Also load the scene's director layer onto the returned record.</param>
    public async Task<SceneRecord> SaveAsync(Scene message, CancellationToken cancellationToken, bool includeDirection = false)
    {
        var sceneId = StoryIds.Existing(message.Id, StoryErrors.SceneNotFound);
        var title = OutlineText.SceneTitle(message.Title);

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(SaveAsync));
        var storyId = await sceneRepository.GetFirstByQueryAsync(
                          sceneQueries.Create().WithId(sceneId), s => (Guid?)s.Container.StoryId, cancellationToken)
                      ?? throw StoryErrors.NotFound(StoryErrors.SceneNotFound);
        await storyScope.LockAsync(storyId, cancellationToken);

        var query = sceneQueries.Create().WithId(sceneId).WithElements();
        if (includeDirection)
        {
            query = query.WithDirection();
        }

        var scene = await sceneRepository.GetFirstByQueryAsync(query, cancellationToken)
                    ?? throw StoryErrors.NotFound(StoryErrors.SceneNotFound);
        var references = await storyScope.ReferencesAsync(storyId, cancellationToken);
        var stored = scene.Elements.ToDictionary(e => e.Id);
        var elements = SceneValidator.Validate(message.Elements, references, stored);

        scene.Title = title;
        var kept = elements.Select(e => e.Id).ToHashSet();
        foreach (var stale in scene.Elements.Where(e => !kept.Contains(e.Id)).ToList())
        {
            await sceneElementRepository.RemoveAsync(stale, cancellationToken);
            scene.Elements.Remove(stale);
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
                scene.Elements.Add(element);
            }
        }

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (StoryErrors.IsUniqueViolation(exception))
        {
            // Client-picked ids can collide with rows the caller cannot see.
            throw StoryErrors.Invalid("Another scene already uses one of these rows.");
        }

        return scene;
    }

    private static void Overwrite(SceneElementRecord stored, SceneElementRecord element)
    {
        stored.Position = element.Position;
        stored.Type = element.Type;
        stored.Text = element.Text;
        stored.LocationId = element.LocationId;
        stored.TimeOfDay = element.TimeOfDay;
        stored.SpeakerId = element.SpeakerId;
        stored.Extension = element.Extension;
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
}
