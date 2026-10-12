using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>
/// Turns a scene's reading into rows: links or adds its parts and objects, then writes one
/// observation per edge in place of the ones the scene's beats had. An edge that names what the
/// story does not have is skipped and reported, never guessed.
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

    public async Task<AppliedScene> ApplyAsync(StorySnapshot story, SceneInfo scene, SceneReading reading, CancellationToken ct)
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
        var objectRefs = new Dictionary<string, Guid>(StringComparer.Ordinal);

        var partsAdded = 0;
        var added = new List<(ReadPart Read, LocationPart Row)>();
        foreach (var read in reading.Parts)
        {
            var name = Clean(read.Name);
            var row = name is null
                ? null
                : storyParts.FirstOrDefault(p => p.LocationId == here && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new LocationPart
                {
                    Id = Guid.NewGuid(),
                    LocationId = here,
                    Name = name,
                    Description = Clean(read.Description),
                    Position = read.Position is >= 0 ? read.Position : null,
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
            if (ResolvePart(read.ParentPartId) is { } parent && parent != row.Id && storyParts.Any(p => p.Id == parent && p.LocationId == here))
            {
                row.ParentPartId = parent;
            }
        }

        var objectsAdded = 0;
        var aliasesAdded = 0;
        foreach (var read in reading.Objects)
        {
            if (Clean(read.Name) is not { } name)
            {
                skipped.Add("an object with no name");
                continue;
            }

            var aliases = (read.Aliases ?? []).Select(Clean).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var row = storyObjects.FirstOrDefault(o => Names(o).Contains(name, StringComparer.OrdinalIgnoreCase))
                      ?? storyObjects.FirstOrDefault(o => aliases.Any(a => Names(o).Contains(a, StringComparer.OrdinalIgnoreCase)));
            if (row is null)
            {
                row = new LocationObject
                {
                    Id = Guid.NewGuid(),
                    StoryId = story.StoryId,
                    Name = name,
                    Aliases = aliases.Where(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).ToList(),
                    Kind = Enum.TryParse<ObjectKind>(read.Kind, ignoreCase: true, out var kind) ? kind : ObjectKind.Prop,
                    Description = Clean(read.Description),
                    PropId = story.Props.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Id,
                };
                await objects.AddAsync(row, ct);
                storyObjects.Add(row);
                objectsAdded++;
            }
            else
            {
                aliasesAdded += AddAliases(row, aliases.Append(name));
                row.Description ??= Clean(read.Description);
            }

            if (Clean(read.Ref) is { } reference)
            {
                objectRefs[reference] = row.Id;
            }
        }

        foreach (var read in reading.Aliases)
        {
            if (ResolveObject(read.ObjectId) is { } objectId && Clean(read.Alias) is { } alias)
            {
                aliasesAdded += AddAliases(storyObjects.First(o => o.Id == objectId), [alias]);
            }
            else
            {
                skipped.Add($"alias \"{read.Alias}\" for unknown object {read.ObjectId}");
            }
        }

        var written = 0;
        foreach (var edge in reading.Edges)
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
                _ => $"unknown edge \"{edge.Edge}\"",
            };
            if (problem is not null)
            {
                skipped.Add(problem);
                continue;
            }

            await observations.AddAsync(row, ct);
            written++;
        }

        await unitOfWork.CommitAsync(ct);
        return new AppliedScene(partsAdded, objectsAdded, aliasesAdded, written, skipped);

        string? At(ReadEdge edge, LocationObservation row)
        {
            if (ResolveObject(edge.ObjectId) is not { } objectId)
            {
                return $"at: unknown object {edge.ObjectId}";
            }

            row.Edge = ObservationEdge.At;
            row.ObjectId = objectId;
            (row.PlaceLocationId, row.PlacePartId) = ResolvePlace(edge.LocationId, edge.PartId, fallback: null);
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

        // A part names its own location; otherwise the location given, or the fallback.
        (Guid?, Guid?) ResolvePlace(string? locationToken, string? partToken, Guid? fallback)
        {
            if (ResolvePart(partToken) is { } partId)
            {
                return (storyParts.First(p => p.Id == partId).LocationId, partId);
            }

            return (ResolveLocation(locationToken) ?? fallback, null);
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
            if (before.LocationId != after.LocationId)
            {
                problems.Add("next: parts of different locations");
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
            var inner = ResolveLocation(edge.LocationId) ?? here;
            if (ResolveLocation(edge.InsideLocationId) is not { } outer || inner == outer)
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

        Guid? ResolveLocation(string? token) =>
            Guid.TryParse(Clean(token), out var id) && storyLocations.ContainsKey(id) ? id : null;
    }

    private static int AddAliases(LocationObject row, IEnumerable<string> aliases)
    {
        var fresh = aliases
            .Where(a => !Names(row).Contains(a, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (fresh.Count > 0)
        {
            row.Aliases = [.. row.Aliases, .. fresh];
        }

        return fresh.Count;
    }

    private static IEnumerable<string> Names(LocationObject o) => o.Aliases.Prepend(o.Name);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
