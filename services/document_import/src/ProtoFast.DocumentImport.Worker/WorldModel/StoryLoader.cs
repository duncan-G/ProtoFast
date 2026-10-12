using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.Data.ThePlot.WorldModel;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>Reads a story and its graph for the build. Runs as the story's owner, so a missing story reads as null.</summary>
public sealed class StoryLoader(
    IUnitOfWorkFactory unitOfWorkFactory,
    IStoryRepository stories,
    IQueryFactory<Story, IStoryQuery> storyQueries,
    ISceneRepository scenes,
    IQueryFactory<Scene, ISceneQuery> sceneQueries,
    ILocationRepository locations,
    IQueryFactory<Location, ILocationQuery> locationQueries,
    ILocationPartRepository parts,
    IQueryFactory<LocationPart, ILocationPartQuery> partQueries,
    ILocationObjectRepository objects,
    IQueryFactory<LocationObject, ILocationObjectQuery> objectQueries,
    ILocationObservationRepository observations,
    IQueryFactory<LocationObservation, ILocationObservationQuery> observationQueries)
{
    private static readonly SceneElementType[] BeatTypes =
        [SceneElementType.Action, SceneElementType.Description, SceneElementType.Narration, SceneElementType.Dialogue];

    public async Task<StorySnapshot?> LoadStoryAsync(Guid storyId, CancellationToken ct)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(LoadStoryAsync));
        var story = await stories.GetFirstByQueryAsync(storyQueries.Create().WithId(storyId).WithLibrary(), ct);
        if (story is null)
        {
            return null;
        }

        var rows = await scenes.GetByQueryAsync(
            sceneQueries.Create().InStory(storyId).InOrder(),
            s => new
            {
                s.Id,
                s.Title,
                Container = s.Container.Position,
                s.Position,
                Elements = s.Elements.OrderBy(e => e.Position).ThenBy(e => e.Id).Select(e => new
                {
                    e.Id,
                    e.Type,
                    e.Text,
                    e.LocationId,
                    e.TimeOfDay,
                    Speaker = e.Speaker != null ? e.Speaker.Name : null,
                    e.Parenthetical,
                }).ToList(),
            },
            ct);

        var sceneInfos = rows.Select(s =>
        {
            var heading = s.Elements.FirstOrDefault(e => e.Type == SceneElementType.Heading);
            var beats = s.Elements
                .Where(e => BeatTypes.Contains(e.Type) && !string.IsNullOrWhiteSpace(e.Text))
                .Select(e => new BeatInfo(e.Id, e.Type, e.Text!, e.Speaker, e.Parenthetical))
                .ToList();
            return new SceneInfo(s.Id, s.Title, s.Container, s.Position, heading?.LocationId, heading?.TimeOfDay, beats);
        }).ToList();

        return new StorySnapshot(
            story.Id,
            story.Title,
            story.Locations.Select(l => new LocationInfo(l.Id, l.Name, l.InsideLocationId)).ToList(),
            story.Characters.Select(c => new CharacterInfo(c.Id, c.Name)).ToList(),
            story.Props.Select(p => new PropInfo(p.Id, p.Name)).ToList(),
            sceneInfos);
    }

    public async Task<GraphSnapshot> LoadGraphAsync(Guid storyId, CancellationToken ct)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(LoadGraphAsync));
        var locationRows = await locations.GetByQueryAsync(
            locationQueries.Create().InStory(storyId).ByName(), l => new { l.Id, l.Name, l.InsideLocationId }, ct);
        var partRows = await parts.GetByQueryAsync(
            partQueries.Create().InStory(storyId).InOrder(),
            p => new { p.Id, p.LocationId, p.ParentPartId, p.Name, p.Description, p.Position },
            ct);
        var objectRows = await objects.GetByQueryAsync(
            objectQueries.Create().InStory(storyId).ByName(),
            o => new { o.Id, o.Name, o.Aliases, o.Kind, o.Description },
            ct);
        var observationRows = await observations.GetByQueryAsync(
            observationQueries.Create().InStory(storyId).InStoryOrder(),
            o => new
            {
                o.Id,
                o.SceneElementId,
                o.SceneElement.SceneId,
                Container = o.SceneElement.Scene.Container.Position,
                Scene = o.SceneElement.Scene.Position,
                Beat = o.SceneElement.Position,
                o.Edge,
                o.ObjectId,
                o.CharacterId,
                o.PlaceLocationId,
                o.PlacePartId,
                o.OtherLocationId,
                o.OtherPartId,
                o.ViaObjectId,
                o.Property,
                o.Value,
                o.Basis,
                o.Origin,
            },
            ct);

        return new GraphSnapshot(
            locationRows.Select(l => new LocationInfo(l.Id, l.Name, l.InsideLocationId)).ToList(),
            partRows.Select(p => new PartInfo(p.Id, p.LocationId, p.ParentPartId, p.Name, p.Description, p.Position)).ToList(),
            objectRows.Select(o => new ObjectInfo(o.Id, o.Name, o.Aliases, o.Kind, o.Description)).ToList(),
            observationRows.Select(o => new ObservationRow(
                o.Id, o.SceneElementId, o.SceneId, new StoryTime(o.Container, o.Scene, o.Beat), o.Edge,
                o.ObjectId, o.CharacterId, o.PlaceLocationId, o.PlacePartId, o.OtherLocationId, o.OtherPartId,
                o.ViaObjectId, o.Property, o.Value, o.Basis, o.Origin)).ToList());
    }
}
