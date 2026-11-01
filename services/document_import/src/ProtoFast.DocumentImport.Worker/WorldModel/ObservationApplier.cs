using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>
/// Turns a scene's reading into rows: links or adds its parts and objects, then writes one
/// observation per edge in place of the ones the scene's beats had. A part named like one of the
/// story's locations is that location, and one named after it ("lobby: counter") is a part of it.
/// A new object is linked by name only to one the scene can reach, and no two objects share an
/// alias. A held object follows its holder through the scene, and no one is placed by their own
/// voice-over line. An edge that names what the story does not have is skipped and reported, never
/// guessed, and so are beats the reading gave no camera.
/// </summary>
public sealed class ObservationApplier(
    IUnitOfWorkFactory unitOfWorkFactory,
    ILocationRepository locations,
    IQueryFactory<Location, ILocationQuery> locationQueries,
    ILocationPartRepository parts,
    IQueryFactory<LocationPart, ILocationPartQuery> partQueries,
    ILocationObjectRepository objects,
    IQueryFactory<LocationObject, ILocationObjectQuery> objectQueries,
    ILocationObservationRepository observations,
    IQueryFactory<LocationObservation, ILocationObservationQuery> observationQueries)
{
    private const string Closed = "closed";

    private static readonly HashSet<string> Pronouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "he", "she", "it", "they", "him", "her", "them", "his", "hers", "its", "their", "this", "that", "these", "those",
    };

    /// <param name="reachable">Objects placed in the scene's location before, and those its characters were last carrying: the only ones a new object links to by name.</param>
    public async Task<AppliedScene> ApplyAsync(
        StorySnapshot story, SceneInfo scene, SceneReading reading, IReadOnlySet<Guid> reachable, CancellationToken ct)
    {
        var here = scene.LocationId ?? throw new InvalidOperationException("The scene has no location.");
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(ApplyAsync));
        var storyLocations = (await locations.GetByQueryAsync(locationQueries.Create().InStory(story.StoryId), ct)).ToDictionary(l => l.Id);
        var storyParts = (await parts.GetByQueryAsync(partQueries.Create().InStory(story.StoryId), ct)).ToList();
        var storyObjects = (await objects.GetByQueryAsync(objectQueries.Create().InStory(story.StoryId), ct)).ToList();
        await observations.DeleteByQueryAsync(observationQueries.Create().InScene(scene.Id).NotAuthored(), ct);

        var characters = story.Characters.Select(c => c.Id).ToHashSet();
        var beats = scene.Beats.Select(b => b.Id).ToHashSet();
        var skipped = new List<string>();
        var partRefs = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var locationRefs = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var objectRefs = new Dictionary<string, Guid>(StringComparer.Ordinal);

        var shadows = new HashSet<ReadPart>();
        foreach (var read in reading.Parts)
        {
            if (LocationNames.Shadowed(Clean(read.Name), LocationOf(read.LocationId) ?? here, storyLocations.Values) is not { } location)
            {
                continue;
            }

            shadows.Add(read);
            if (Clean(read.Ref) is { } reference)
            {
                locationRefs[reference] = location;
            }
        }

        var partsAdded = 0;
        var added = new List<(ReadPart Read, LocationPart Row)>();
        foreach (var read in reading.Parts.Where(r => !shadows.Contains(r)))
        {
            var name = Clean(read.Name);
            var owner = LocationOf(read.LocationId) ?? LocationOf(read.ParentPartId) ?? here;
            if (LocationNames.Prefixed(name, owner, storyLocations.Values) is { } prefixed)
            {
                (owner, name) = prefixed;
            }

            var row = name is null
                ? null
                : storyParts.FirstOrDefault(p => p.LocationId == owner && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new LocationPart
                {
                    Id = Guid.NewGuid(),
                    LocationId = owner,
                    Name = name,
                    Description = Clean(read.Description),
                    Position = read.Position is >= 0 && SpaceOf(name) is not null ? read.Position : null,
                    Origin = NodeOrigin.Extracted,
                };
                await parts.AddAsync(row, ct);
                storyParts.Add(row);
                added.Add((read, row));
                partsAdded++;
            }
            else if (row.Description is null)
            {
                row.Description = Clean(read.Description);
            }

            if (Clean(read.Ref) is { } reference)
            {
                partRefs[reference] = row.Id;
            }
        }

        // Parents may be refs of parts added above, so they resolve once every part is known.
        foreach (var (read, row) in added)
        {
            if (ResolvePart(read.ParentPartId) is { } parent && parent != row.Id && storyParts.Any(p => p.Id == parent && p.LocationId == row.LocationId))
            {
                row.ParentPartId = parent;
            }
        }

        var objectsAdded = 0;
        var aliasesAdded = 0;
        var addedObjects = new HashSet<Guid>();
        foreach (var read in reading.Objects)
        {
            if (Clean(read.Name) is not { } name)
            {
                skipped.Add("an object with no name");
                continue;
            }

            var aliases = (read.Aliases ?? []).Select(Clean).OfType<string>().Where(a => !Pronouns.Contains(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var kind = Enum.TryParse<ObjectKind>(read.Kind, ignoreCase: true, out var parsed) ? parsed : ObjectKind.Prop;
            // By name only while no object is that prop yet: a second thing of the same name is another one.
            var prop = story.Props.FirstOrDefault(p => string.Equals(p.Name, Clean(read.Prop), StringComparison.OrdinalIgnoreCase))
                       ?? story.Props.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                                                          && storyObjects.All(o => o.PropId != p.Id));
            var candidates = storyObjects.Where(o => reachable.Contains(o.Id) || addedObjects.Contains(o.Id));
            var row = candidates.FirstOrDefault(o => Names(o).Contains(name, StringComparer.OrdinalIgnoreCase))
                      ?? candidates.FirstOrDefault(o => aliases.Any(a => Names(o).Contains(a, StringComparer.OrdinalIgnoreCase)));
            if (row is null)
            {
                row = new LocationObject
                {
                    Id = Guid.NewGuid(),
                    StoryId = story.StoryId,
                    Name = name,
                    Aliases = aliases.Where(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase) && !Taken(a, null)).ToList(),
                    Kind = kind,
                    Description = Clean(read.Description),
                    PropId = prop?.Id,
                };
                await objects.AddAsync(row, ct);
                storyObjects.Add(row);
                addedObjects.Add(row.Id);
                objectsAdded++;
            }
            else
            {
                aliasesAdded += AddAliases(row, aliases.Append(name));
                row.Description ??= Clean(read.Description);
                row.PropId ??= prop?.Id;
            }

            if (Clean(read.Ref) is { } reference)
            {
                objectRefs[reference] = row.Id;
            }
        }

        foreach (var read in reading.Aliases)
        {
            if (ResolveObject(read.ObjectId) is { } objectId && Clean(read.Alias) is { } alias && !Pronouns.Contains(alias))
            {
                aliasesAdded += AddAliases(storyObjects.First(o => o.Id == objectId), [alias]);
            }
            else
            {
                skipped.Add($"alias \"{read.Alias}\" for unknown object {read.ObjectId}");
            }
        }

        var voiced = scene.Beats.Where(b => b.IsVoiceOver && b.SpeakerId is not null).Select(b => (b.Id, b.SpeakerId!.Value)).ToHashSet();
        var rows = new List<LocationObservation>();
        foreach (var edge in reading.AllEdges())
        {
            var kind = (edge.Edge ?? "").Trim().ToLowerInvariant().Replace('-', '_');
            switch (kind)
            {
                case "next":
                    Order(edge, skipped);
                    continue;
                case "inside":
                    Nest(edge, skipped);
                    continue;
            }

            if (!Guid.TryParse(edge.BeatId, out var beatId) || !beats.Contains(beatId))
            {
                skipped.Add($"{kind} on unknown beat {edge.BeatId}");
                continue;
            }

            var row = new LocationObservation
            {
                Id = Guid.NewGuid(),
                SceneElementId = beatId,
                Basis = string.Equals(edge.Basis, "stated", StringComparison.OrdinalIgnoreCase) ? ObservationBasis.Stated : ObservationBasis.Implied,
                Origin = ObservationOrigin.Extracted,
            };
            string? problem = kind switch
            {
                "at" => At(edge, row),
                "state" => State(edge, row),
                "connects" => Connects(edge, row),
                "character_at" => CharacterAt(edge, row),
                "beat_at" => BeatAt(edge, row),
                "camera" => Camera(edge, row),
                _ => $"unknown edge \"{edge.Edge}\"",
            };
            if (problem is not null)
            {
                skipped.Add(problem);
                continue;
            }

            rows.Add(row);
        }

        rows.AddRange(Carry(scene, rows, here));
        foreach (var row in rows)
        {
            await observations.AddAsync(row, ct);
        }

        await unitOfWork.CommitAsync(ct);
        var filmed = rows.Where(r => r.Edge == ObservationEdge.Camera).Select(r => r.SceneElementId).ToHashSet();
        return new AppliedScene(partsAdded, objectsAdded, aliasesAdded, rows.Count, skipped, beats.Count(b => !filmed.Contains(b)));

        string? At(ReadEdge edge, LocationObservation row)
        {
            if (ResolveObject(edge.ObjectId) is not { } objectId)
            {
                return $"at: unknown object {edge.ObjectId}";
            }

            row.Edge = ObservationEdge.At;
            row.ObjectId = objectId;
            (row.PlaceLocationId, row.PlacePartId) = ResolvePlace(edge.LocationId, edge.PartId, fallback: null);
            if (Clean(edge.CharacterId) is { } holder)
            {
                if (!Guid.TryParse(holder, out var holderId) || !characters.Contains(holderId))
                {
                    return $"at: unknown holder {edge.CharacterId}";
                }

                row.CharacterId = holderId;
            }

            return null;
        }

        string? State(ReadEdge edge, LocationObservation row)
        {
            if (ResolveObject(edge.ObjectId) is not { } objectId)
            {
                return $"state: unknown object {edge.ObjectId}";
            }

            if (Clean(edge.Property) is not { } property || Clean(edge.Value) is not { } value)
            {
                return "state: no property or value";
            }

            row.Edge = ObservationEdge.State;
            row.ObjectId = objectId;
            row.Property = property;
            row.Value = value;
            return null;
        }

        string? Connects(ReadEdge edge, LocationObservation row)
        {
            var (location, part) = ResolvePlace(edge.LocationId, edge.PartId, fallback: here);
            var (other, otherPart) = ResolvePlace(edge.OtherLocationId, edge.OtherPartId, fallback: null);
            if (location is null || other is null)
            {
                return "connects: an end is not a place in the story";
            }

            if (location == other && part == otherPart)
            {
                return "connects: both ends are the same place";
            }

            row.Edge = ObservationEdge.Connects;
            row.PlaceLocationId = location;
            row.PlacePartId = part;
            row.OtherLocationId = other;
            row.OtherPartId = otherPart;
            row.ViaObjectId = ResolveObject(edge.ViaObjectId);
            row.Value = string.Equals(Clean(edge.Value), Closed, StringComparison.OrdinalIgnoreCase) ? Closed : null;
            return null;
        }

        string? CharacterAt(ReadEdge edge, LocationObservation row)
        {
            if (!Guid.TryParse(edge.CharacterId, out var characterId) || !characters.Contains(characterId))
            {
                return $"character_at: unknown character {edge.CharacterId}";
            }

            if (voiced.Contains((row.SceneElementId, characterId)))
            {
                return $"character_at: {story.Characters.First(c => c.Id == characterId).Name} only speaks over this beat";
            }

            var (location, part) = ResolvePlace(edge.LocationId, edge.PartId, fallback: here);
            row.Edge = ObservationEdge.CharacterAt;
            row.CharacterId = characterId;
            row.PlaceLocationId = location;
            row.PlacePartId = part;
            return null;
        }

        string? BeatAt(ReadEdge edge, LocationObservation row)
        {
            var (location, part) = ResolvePlace(edge.LocationId, edge.PartId, fallback: here);
            row.Edge = ObservationEdge.BeatAt;
            row.PlaceLocationId = location;
            row.PlacePartId = part;
            return null;
        }

        string? Camera(ReadEdge edge, LocationObservation row)
        {
            var (location, part) = ResolvePlace(edge.LocationId, edge.PartId, fallback: here);
            var (toward, towardPart) = ResolvePlace(edge.TowardLocationId, edge.TowardPartId, fallback: null);
            row.Edge = ObservationEdge.Camera;
            row.PlaceLocationId = location;
            row.PlacePartId = part;
            if (toward != location || towardPart != part)
            {
                row.OtherLocationId = toward;
                row.OtherPartId = towardPart;
            }

            return null;
        }

        // A part names its own location; otherwise the location given, or the fallback.
        (Guid?, Guid?) ResolvePlace(string? locationToken, string? partToken, Guid? fallback)
        {
            if (ResolvePart(partToken) is { } partId)
            {
                return (storyParts.First(p => p.Id == partId).LocationId, partId);
            }

            return (LocationOf(partToken) ?? LocationOf(locationToken) ?? fallback, null);
        }

        void Order(ReadEdge edge, List<string> problems)
        {
            if (ResolvePart(edge.PartId) is not { } first || ResolvePart(edge.NextPartId) is not { } second || first == second)
            {
                problems.Add($"next: unknown parts {edge.PartId} → {edge.NextPartId}");
                return;
            }

            var before = storyParts.First(p => p.Id == first);
            var after = storyParts.First(p => p.Id == second);
            // Two bare positions ("start", "end") order the location itself, a corridor or a road.
            if (before.LocationId != after.LocationId
                || !string.Equals(SpaceOf(before.Name), SpaceOf(after.Name), StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"next: \"{before.Name}\" and \"{after.Name}\" are not positions of one space");
                return;
            }

            before.Position ??= 0;
            if (after.Position is null || after.Position <= before.Position)
            {
                after.Position = before.Position + 1;
            }
        }

        void Nest(ReadEdge edge, List<string> problems)
        {
            var inner = LocationOf(edge.LocationId) ?? here;
            if (LocationOf(edge.InsideLocationId) is not { } outer || inner == outer)
            {
                problems.Add($"inside: unknown or same location {edge.InsideLocationId}");
                return;
            }

            var location = storyLocations[inner];
            if (location.InsideLocationId is not null)
            {
                return;
            }

            for (var above = storyLocations.GetValueOrDefault(outer); above is not null; above = above.InsideLocationId is { } up ? storyLocations.GetValueOrDefault(up) : null)
            {
                if (above.Id == inner)
                {
                    problems.Add("inside: would make a loop");
                    return;
                }
            }

            location.InsideLocationId = outer;
        }

        // A token is a ref from this reading or the id of a row the story has.
        Guid? ResolvePart(string? token)
        {
            if (Clean(token) is not { } t)
            {
                return null;
            }

            if (partRefs.TryGetValue(t, out var byRef))
            {
                return byRef;
            }

            return Guid.TryParse(t, out var id) && storyParts.Any(p => p.Id == id) ? id : null;
        }

        Guid? ResolveObject(string? token)
        {
            if (Clean(token) is not { } t)
            {
                return null;
            }

            if (objectRefs.TryGetValue(t, out var byRef))
            {
                return byRef;
            }

            return Guid.TryParse(t, out var id) && storyObjects.Any(o => o.Id == id) ? id : null;
        }

        int AddAliases(LocationObject row, IEnumerable<string> aliases)
        {
            var fresh = aliases
                .Where(a => !Names(row).Contains(a, StringComparer.OrdinalIgnoreCase) && !Taken(a, row))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (fresh.Count > 0)
            {
                row.Aliases = [.. row.Aliases, .. fresh];
            }

            return fresh.Count;
        }

        bool Taken(string alias, LocationObject? by) =>
            storyObjects.Any(o => o != by && Names(o).Contains(alias, StringComparer.OrdinalIgnoreCase));

        // A location's id, or the ref of a part that turned out to be a location.
        Guid? LocationOf(string? token) => Clean(token) switch
        {
            null => null,
            var t when locationRefs.TryGetValue(t, out var location) => location,
            var t => Guid.TryParse(t, out var id) && storyLocations.ContainsKey(id) ? id : null,
        };
    }

    /// <summary>
    /// Moves each held object with its holder, beat by beat: an at observation follows every
    /// character_at of the holder that the reading gave no at for. A held object with no place
    /// given is where its holder is.
    /// </summary>
    public static IEnumerable<LocationObservation> Carry(SceneInfo scene, IReadOnlyList<LocationObservation> rows, Guid here)
    {
        var order = scene.Beats.Select((b, i) => (b.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var where = new Dictionary<Guid, (Guid? Location, Guid? Part)>();
        var held = new Dictionary<Guid, Guid>();
        var carried = new List<LocationObservation>();
        foreach (var beat in rows.GroupBy(r => r.SceneElementId).OrderBy(g => order.GetValueOrDefault(g.Key)))
        {
            foreach (var row in beat.Where(r => r.Edge == ObservationEdge.CharacterAt))
            {
                where[row.CharacterId!.Value] = (row.PlaceLocationId, row.PlacePartId);
            }

            var placed = new HashSet<Guid>();
            foreach (var row in beat.Where(r => r.Edge == ObservationEdge.At))
            {
                var objectId = row.ObjectId!.Value;
                placed.Add(objectId);
                if (row.CharacterId is not { } holder)
                {
                    held.Remove(objectId);
                    continue;
                }

                if (row.PlaceLocationId is null)
                {
                    (row.PlaceLocationId, row.PlacePartId) = where.GetValueOrDefault(holder, (here, null as Guid?));
                }

                held[objectId] = holder;
            }

            foreach (var moved in beat.Where(r => r.Edge == ObservationEdge.CharacterAt))
            {
                foreach (var (objectId, _) in held.Where(h => h.Value == moved.CharacterId && !placed.Contains(h.Key)))
                {
                    carried.Add(new LocationObservation
                    {
                        Id = Guid.NewGuid(),
                        SceneElementId = beat.Key,
                        Edge = ObservationEdge.At,
                        ObjectId = objectId,
                        CharacterId = moved.CharacterId,
                        PlaceLocationId = moved.PlaceLocationId,
                        PlacePartId = moved.PlacePartId,
                        Basis = ObservationBasis.Implied,
                        Origin = ObservationOrigin.Extracted,
                    });
                }
            }
        }

        return carried;
    }

    /// <summary>"hallway" for "hallway: start": the linear space a position belongs to.</summary>
    private static string? SpaceOf(string? name) =>
        name?.IndexOf(':') is > 0 and var colon ? name[..colon].Trim() : null;

    private static IEnumerable<string> Names(LocationObject o) => o.Aliases.Prepend(o.Name);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
