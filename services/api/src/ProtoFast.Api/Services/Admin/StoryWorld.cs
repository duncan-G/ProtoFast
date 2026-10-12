using Microsoft.EntityFrameworkCore;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Data.ThePlot;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.WorldModel;

namespace ProtoFast.Api.Services.Admin;

using LocationSetting = ProtoFast.Data.ThePlot.Entities.LocationSetting;
using SceneElementType = ProtoFast.Data.ThePlot.Entities.SceneElementType;

/// <summary>
/// Story time as moments (each scene's opening, then its beats) and every fact over them. Reads
/// every writer's rows, so the per-user query filters are ignored.
/// </summary>
internal static class StoryWorld
{
    private static readonly SceneElementType[] BeatTypes =
        [SceneElementType.Action, SceneElementType.Description, SceneElementType.Narration, SceneElementType.Dialogue];

    public static async Task<GetWorldReply?> ReadAsync(ThePlotDbContext theplot, Guid storyId, CancellationToken ct)
    {
        var story = await theplot.Stories.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == storyId)
            .Select(s => new { s.Id, s.Title, s.UserId })
            .FirstOrDefaultAsync(ct);
        if (story is null)
        {
            return null;
        }

        var sceneRows = await theplot.Scenes.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Container.StoryId == storyId)
            .OrderBy(s => s.Container.Position).ThenBy(s => s.Position).ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.Title,
                Container = s.Container.Position,
                s.Position,
                Heading = s.Elements
                    .Where(e => e.Type == SceneElementType.Heading)
                    .OrderBy(e => e.Position).ThenBy(e => e.Id)
                    .Select(e => new { e.LocationId, e.TimeOfDay })
                    .FirstOrDefault(),
                Beats = s.Elements
                    .Where(e => BeatTypes.Contains(e.Type))
                    .OrderBy(e => e.Position).ThenBy(e => e.Id)
                    .Select(e => new { e.Id, e.Position, e.Text, Speaker = e.Speaker != null ? e.Speaker.Name : null })
                    .ToList(),
            })
            .ToListAsync(ct);
        var locations = await theplot.Locations.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.StoryId == storyId)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);
        var characters = await theplot.Characters.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.StoryId == storyId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        var parts = await theplot.LocationParts.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Location.StoryId == storyId)
            .OrderBy(p => p.LocationId).ThenBy(p => p.Position).ThenBy(p => p.Name)
            .ToListAsync(ct);
        var objects = await theplot.LocationObjects.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.StoryId == storyId)
            .OrderBy(o => o.Name)
            .ToListAsync(ct);
        var observations = (await theplot.LocationObservations.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.SceneElement.Scene.Container.StoryId == storyId)
                .OrderBy(o => o.SceneElement.Scene.Container.Position)
                .ThenBy(o => o.SceneElement.Scene.Position)
                .ThenBy(o => o.SceneElement.Position)
                .ThenBy(o => o.Id)
                .Select(o => new
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
                })
                .ToListAsync(ct))
            .Select(o => new ObservationRow(
                o.Id, o.SceneElementId, o.SceneId, new StoryTime(o.Container, o.Scene, o.Beat), o.Edge,
                o.ObjectId, o.CharacterId, o.PlaceLocationId, o.PlacePartId, o.OtherLocationId, o.OtherPartId,
                o.ViaObjectId, o.Property, o.Value, o.Basis, o.Origin))
            .ToList();
        var latestBuild = await theplot.WorldModelBuilds.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.StoryId == storyId)
            .OrderByDescending(b => b.DateCreated).ThenByDescending(b => b.Id)
            .Select(WorldModelAdminMessages.Header)
            .FirstOrDefaultAsync(ct);

        var reply = new GetWorldReply
        {
            StoryId = story.Id.ToString(),
            StoryTitle = story.Title,
            OwnerId = story.UserId,
            LatestBuild = latestBuild,
        };

        var moments = new List<StoryTime>();
        var slots = new List<SceneSlot>();
        foreach (var scene in sceneRows)
        {
            var message = new WorldScene
            {
                Id = scene.Id.ToString(),
                Title = scene.Title,
                LocationId = scene.Heading?.LocationId?.ToString() ?? "",
                TimeOfDay = scene.Heading?.TimeOfDay ?? "",
            };
            moments.Add(StoryTime.SceneStart(scene.Container, scene.Position));
            foreach (var beat in scene.Beats.Where(b => !string.IsNullOrWhiteSpace(b.Text)))
            {
                moments.Add(new StoryTime(scene.Container, scene.Position, beat.Position));
                message.Beats.Add(new WorldBeat { Id = beat.Id.ToString(), Text = beat.Text!, Speaker = beat.Speaker ?? "" });
            }

            reply.Scenes.Add(message);
            slots.Add(new SceneSlot(scene.Id, scene.Heading?.LocationId, scene.Container, scene.Position));
        }

        reply.Locations.AddRange(locations.Select(l => new WorldLocation
        {
            Id = l.Id.ToString(),
            Name = l.Name,
            Exterior = l.Setting == LocationSetting.Exterior,
            Hue = l.Hue,
            InsideLocationId = l.InsideLocationId?.ToString() ?? "",
        }));
        reply.Characters.AddRange(characters.Select(c => new WorldCharacter { Id = c.Id.ToString(), Name = c.Name, Hue = c.Hue }));
        reply.Parts.AddRange(parts.Select(p =>
        {
            var part = new WorldPart
            {
                Id = p.Id.ToString(),
                LocationId = p.LocationId.ToString(),
                ParentPartId = p.ParentPartId?.ToString() ?? "",
                Name = p.Name ?? "",
                Description = p.Description ?? "",
            };
            if (p.Position is { } position)
            {
                part.Position = position;
            }

            return part;
        }));
        reply.Objects.AddRange(objects.Select(o =>
        {
            var message = new WorldObject { Id = o.Id.ToString(), Name = o.Name, Kind = ToKind(o.Kind), Description = o.Description ?? "" };
            message.Aliases.AddRange(o.Aliases);
            return message;
        }));

        var graph = WorldModelFolder.Fold(observations, slots, objects.ToDictionary(o => o.Id, o => o.Kind));
        var said = observations.ToDictionary(o => o.Id, o => o.Time);
        var factIndex = new Dictionary<Guid, int>();
        var messages = new List<WorldFact>();
        foreach (var fact in graph.Facts)
        {
            factIndex[fact.ObservationId] = messages.Count;
            messages.Add(new WorldFact
            {
                Edge = ToEdge(fact.Edge),
                Key = fact.Key,
                ObjectId = Id(fact.ObjectId),
                CharacterId = Id(fact.CharacterId),
                Place = ToPlace(fact.Place),
                Other = ToPlace(fact.Other),
                ViaObjectId = Id(fact.ViaObjectId),
                Property = fact.Property ?? "",
                Value = fact.Value ?? "",
                Basis = ToBasis(fact.Basis),
                Origin = ToOrigin(fact.Origin),
                Start = FirstFrom(moments, fact.Start),
                End = fact.End is { } end ? FirstFrom(moments, end) : moments.Count,
                Said = LastUpTo(moments, said.GetValueOrDefault(fact.ObservationId, fact.Start)),
            });
        }

        var lost = graph.Conflicts.Select(c => c.Observation.Id).ToHashSet();
        var factsByKey = graph.Facts.Select((f, i) => (Fact: f, Index: i)).ToLookup(f => f.Fact.Key);
        foreach (var observation in observations.Where(o => !o.Ends && !lost.Contains(o.Id) && !factIndex.ContainsKey(o.Id)))
        {
            var backed = factsByKey[WorldModelFolder.KeyOf(observation)].FirstOrDefault(f => f.Fact.HoldsAt(observation.Time));
            if (backed.Fact is null)
            {
                continue;
            }

            var moment = LastUpTo(moments, observation.Time);
            var fact = messages[backed.Index];
            if (moment != fact.Said && !fact.Confirmed.Contains(moment))
            {
                fact.Confirmed.Add(moment);
            }
        }

        reply.Facts.AddRange(messages);
        reply.Conflicts.AddRange(graph.Conflicts
            .Where(c => factIndex.ContainsKey(c.Standing.ObservationId))
            .Select(c => new WorldConflict
            {
                Fact = factIndex[c.Standing.ObservationId],
                Moment = LastUpTo(moments, c.Observation.Time),
                Place = ToPlace(c.Observation.Place),
                Other = ToPlace(c.Observation.Other),
                Value = c.Observation.Value ?? "",
                Basis = ToBasis(c.Observation.Basis),
                Origin = ToOrigin(c.Observation.Origin),
                Ends = c.Observation.Ends,
            }));
        return reply;
    }

    /// <summary>The first moment at or after <paramref name="time"/>; the moment count when there is none.</summary>
    private static int FirstFrom(List<StoryTime> moments, StoryTime time)
    {
        var (low, high) = (0, moments.Count);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (moments[middle] < time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>The last moment at or before <paramref name="time"/>.</summary>
    private static int LastUpTo(List<StoryTime> moments, StoryTime time)
    {
        var next = FirstFrom(moments, time);
        return next < moments.Count && moments[next] == time ? next : Math.Max(next - 1, 0);
    }

    private static string Id(Guid? id) => id?.ToString() ?? "";

    private static WorldPlace? ToPlace(PlaceRef? place) =>
        place is { } p ? new WorldPlace { LocationId = p.LocationId.ToString(), PartId = Id(p.PartId) } : null;

    private static WorldEdge ToEdge(ObservationEdge edge) => edge switch
    {
        ObservationEdge.At => WorldEdge.At,
        ObservationEdge.State => WorldEdge.State,
        ObservationEdge.Connects => WorldEdge.Connects,
        ObservationEdge.CharacterAt => WorldEdge.CharacterAt,
        ObservationEdge.BeatAt => WorldEdge.BeatAt,
        _ => WorldEdge.Unspecified,
    };

    private static WorldBasis ToBasis(ObservationBasis basis) => basis switch
    {
        ObservationBasis.Stated => WorldBasis.Stated,
        ObservationBasis.Implied => WorldBasis.Implied,
        _ => WorldBasis.Unspecified,
    };

    private static WorldOrigin ToOrigin(ObservationOrigin origin) => origin switch
    {
        ObservationOrigin.Extracted => WorldOrigin.Extracted,
        ObservationOrigin.Rule => WorldOrigin.Rule,
        ObservationOrigin.Author => WorldOrigin.Author,
        _ => WorldOrigin.Unspecified,
    };

    private static WorldObjectKind ToKind(ObjectKind kind) => kind switch
    {
        ObjectKind.Fixture => WorldObjectKind.Fixture,
        ObjectKind.Prop => WorldObjectKind.Prop,
        ObjectKind.Dressing => WorldObjectKind.Dressing,
        ObjectKind.Occupant => WorldObjectKind.Occupant,
        _ => WorldObjectKind.Unspecified,
    };
}
