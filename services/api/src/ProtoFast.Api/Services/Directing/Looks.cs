using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
using SceneLookChangeRecord = ProtoFast.Data.ThePlot.Entities.SceneLookChange;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// A look lasts until a line changes it: a scene opens with each character's look and every change
/// holds from its line to the next change, across frames and into the next scene. A prop is held
/// by one character at a time.
/// </summary>
public static class Looks
{
    /// <param name="position">An element's position in the scene; changes on lines no longer in it sort last.</param>
    public static LookInForce At(
        IEnumerable<SceneAppearanceRecord> appearances,
        IEnumerable<SceneLookChangeRecord> changes,
        Func<Guid, int> position,
        Guid characterId,
        int at)
    {
        var opening = appearances.FirstOrDefault(a => a.CharacterId == characterId);
        var change = Changes(changes, position, characterId).LastOrDefault(c => position(c.ElementId) <= at);
        var look = change is not null ? Look.Of(change) : opening is not null ? Look.Of(opening) : Look.Plain;
        return new LookInForce(look, opening, change);
    }

    public static LookInForce At(SceneRecord scene, Guid characterId, int at) =>
        At(scene.Appearances, scene.LookChanges, Position(scene), characterId, at);

    /// <summary>The look the scene ends on, or null when the character has none in it.</summary>
    public static Look? Closing(SceneRecord scene, Guid characterId)
    {
        var last = Changes(scene.LookChanges, Position(scene), characterId).LastOrDefault();
        if (last is not null)
        {
            return Look.Of(last);
        }

        return scene.Appearances.FirstOrDefault(a => a.CharacterId == characterId) is { } opening ? Look.Of(opening) : null;
    }

    public static IEnumerable<SceneLookChangeRecord> Changes(
        IEnumerable<SceneLookChangeRecord> changes, Func<Guid, int> position, Guid characterId) =>
        changes.Where(c => c.CharacterId == characterId).OrderBy(c => position(c.ElementId)).ThenBy(c => c.Id);

    public static Func<Guid, int> Position(SceneRecord scene)
    {
        var positions = scene.Elements.ToDictionary(e => e.Id, e => e.Position);
        return id => positions.TryGetValue(id, out var position) ? position : int.MaxValue;
    }
}
