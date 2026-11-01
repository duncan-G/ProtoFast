using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Core;
using ProtoFast.Data.ThePlot.WorldModel;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>
/// One build, end to end (design doc §7): seed, read every scene with the model and apply what it
/// gives, finish, then describe every location the story goes to. A scene is read after every earlier scene that shares its location or one
/// of its characters, so it starts from the graph they left (where people were last seen, what
/// they carry); scenes with nothing in common run side by side. A delivery that follows a
/// failed one skips the scenes already read. Returns false when the build was no longer there
/// to run: cancelled, finished by another delivery, or deleted with its story.
/// </summary>
public sealed class WorldModelBuildRunner(
    IServiceScopeFactory scopes,
    WorldModelLedger ledger,
    SceneReader reader,
    LocationDescriber describer,
    IOptions<WorldModelBuildOptions> options,
    ILogger<WorldModelBuildRunner> logger)
{
    public async Task<bool> RunAsync(WorldModelBuildRequested request, CancellationToken ct)
    {
        var (buildId, storyId, userId) = request;
        if (!await ledger.ClaimAsync(buildId, userId, Activity.Current?.TraceId.ToString(), ct))
        {
            return false;
        }

        var story = await InScopeAsync(userId, (sp, c) => sp.GetRequiredService<StoryLoader>().LoadStoryAsync(storyId, c), ct);
        if (story is null)
        {
            logger.LogInformation("Story {StoryId} of build {BuildId} is gone; nothing to build", storyId, buildId);
            return false;
        }

        var resume = await ledger.ResumeAsync(buildId, userId, ct);
        var sequence = resume.NextSequence - 1;
        await ledger.SetTotalAsync(buildId, userId, story.Scenes.Count, ct);
        if (!resume.Seeded)
        {
            await SeedAsync(buildId, userId, story, Interlocked.Increment(ref sequence), ct);
        }

        foreach (var scene in story.Scenes.Where(s => s.LocationId is null && !resume.HandledScenes.Contains(s.Id)))
        {
            var step = await ledger.BeginStepAsync(buildId, userId, Interlocked.Increment(ref sequence), WorldModelStepKind.ReadScene, scene.Id, scene.Title, ct);
            await ledger.SkipStepAsync(buildId, step, userId, "The scene has no location in its heading.", ct);
        }

        var ordered = story.Scenes.Where(s => s.LocationId is not null).OrderBy(s => s.Slot.Start).ToList();
        var flashbacks = new ConcurrentDictionary<Guid, bool>(resume.Flashbacks.Select(id => KeyValuePair.Create(id, true)));
        using var applying = new SemaphoreSlim(1, 1);
        using var reading = new SemaphoreSlim(Math.Max(1, options.Value.LocationConcurrency));
        var reads = new List<Task>();
        foreach (var (scene, index) in ordered.Select((s, i) => (s, i)))
        {
            var before = ordered.Take(index).Select((s, i) => (s, i)).Where(p => p.s.Meets(scene)).Select(p => reads[p.i]).ToList();
            reads.Add(ReadAfterAsync(scene, before));
        }

        await Task.WhenAll(reads);

        var counts = await FinishAsync(buildId, userId, story, Interlocked.Increment(ref sequence), ct);
        await DescribeLocationsAsync(buildId, userId, story, () => Interlocked.Increment(ref sequence), ct);
        await ledger.FinishAsync(buildId, userId, counts, ct);
        return true;

        async Task ReadAfterAsync(SceneInfo scene, List<Task> before)
        {
            await Task.WhenAll(before);
            if (resume.HandledScenes.Contains(scene.Id))
            {
                return;
            }

            await reading.WaitAsync(ct);
            try
            {
                await ReadSceneAsync(buildId, userId, story, scene, Interlocked.Increment(ref sequence), applying, flashbacks, ct);
            }
            finally
            {
                reading.Release();
            }
        }
    }

    /// <summary>
    /// Nests locations whose names say so: "HOSPITAL – TRAUMA WING" is inside "HOSPITAL" when both
    /// exist. Every scene is read again, so what an earlier build read is cleared first (its
    /// observations, nestings, and the parts and objects only they used): a scene must open on
    /// what this build has read, not on an older reading of scenes it has not reached.
    /// </summary>
    private async Task SeedAsync(Guid buildId, string userId, StorySnapshot story, int sequence, CancellationToken ct)
    {
        var step = await ledger.BeginStepAsync(buildId, userId, sequence, WorldModelStepKind.Seed, null, "Seed", ct);
        try
        {
            var (nested, clearedParts, clearedObjects) = await InScopeAsync(userId, async (sp, c) =>
            {
                var unitOfWorkFactory = sp.GetRequiredService<IUnitOfWorkFactory>();
                var locations = sp.GetRequiredService<ILocationRepository>();
                var queries = sp.GetRequiredService<IQueryFactory<Location, ILocationQuery>>();
                var parts = sp.GetRequiredService<ILocationPartRepository>();
                var partQueries = sp.GetRequiredService<IQueryFactory<LocationPart, ILocationPartQuery>>();
                var objects = sp.GetRequiredService<ILocationObjectRepository>();
                var objectQueries = sp.GetRequiredService<IQueryFactory<LocationObject, ILocationObjectQuery>>();
                var observations = sp.GetRequiredService<ILocationObservationRepository>();
                var observationQueries = sp.GetRequiredService<IQueryFactory<LocationObservation, ILocationObservationQuery>>();

                using (var clearing = unitOfWorkFactory.CreateReadWrite(nameof(SeedAsync)))
                {
                    await observations.DeleteByQueryAsync(observationQueries.Create().InStory(story.StoryId).NotAuthored(), c);
                    await clearing.CommitAsync(c);
                }

                int sweptParts, sweptObjects;
                using (var sweep = unitOfWorkFactory.CreateReadWrite(nameof(SeedAsync)))
                {
                    sweptParts = await parts.DeleteByQueryAsync(partQueries.Create().InStory(story.StoryId).OfOrigin(NodeOrigin.Extracted).Unobserved(), c);
                    sweptObjects = await objects.DeleteByQueryAsync(objectQueries.Create().InStory(story.StoryId).Unobserved(), c);
                    await sweep.CommitAsync(c);
                }

                using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(SeedAsync));
                var rows = await locations.GetByQueryAsync(queries.Create().InStory(story.StoryId), c);
                var count = 0;
                foreach (var location in rows)
                {
                    location.InsideLocationId = null;
                }

                foreach (var location in rows)
                {
                    if (LocationNames.Split(location.Name) is not { } split)
                    {
                        continue;
                    }

                    var outer = rows.FirstOrDefault(l => l.Id != location.Id && string.Equals(l.Name, split.Outer, StringComparison.OrdinalIgnoreCase));
                    if (outer is not null)
                    {
                        location.InsideLocationId = outer.Id;
                        count++;
                    }
                }

                await unitOfWork.CommitAsync(c);
                return (count, sweptParts, sweptObjects);
            }, ct);

            var summary = $"{story.Locations.Count} locations, {story.Characters.Count} characters, {story.Scenes.Count} scenes; "
                          + $"{nested} {(nested == 1 ? "location" : "locations")} nested by name"
                          + (clearedParts + clearedObjects > 0
                              ? $"; cleared the last build's {clearedParts} {(clearedParts == 1 ? "part" : "parts")} and {clearedObjects} {(clearedObjects == 1 ? "object" : "objects")}"
                              : "");
            await ledger.FinishStepAsync(buildId, step, userId, new StepOutcome(null, 0, 0, 0, null, null, summary), sceneDone: false, ct);
        }
        catch (Exception e)
        {
            await TryFailStepAsync(step, userId, e);
            throw;
        }
    }

    private async Task ReadSceneAsync(
        Guid buildId, string userId, StorySnapshot story, SceneInfo scene, int sequence, SemaphoreSlim applying,
        ConcurrentDictionary<Guid, bool> flashbacks, CancellationToken ct)
    {
        var step = await ledger.BeginStepAsync(buildId, userId, sequence, WorldModelStepKind.ReadScene, scene.Id, scene.Title, ct);
        try
        {
            if (scene.Beats.Count == 0)
            {
                await ledger.SkipStepAsync(buildId, step, userId, "The scene has no beats.", ct);
                return;
            }

            var graph = await InScopeAsync(userId, (sp, c) => sp.GetRequiredService<StoryLoader>().LoadGraphAsync(story.StoryId, c), ct);
            var facts = WorldModelFolder.Fold(graph.Observations, story.Scenes.Select(s => s.Slot).ToList(), graph.Objects.ToDictionary(o => o.Id, o => o.Kind));
            var earlier = flashbacks.Keys.ToHashSet();
            var read = await reader.ReadAsync(story, scene, graph, facts, earlier, ct);
            if (read.Reading.Flashback == true)
            {
                flashbacks[scene.Id] = true;
            }

            var here = graph.Locations.First(l => l.Id == scene.LocationId);
            var around = graph.Locations
                .Where(l => l.Id == scene.LocationId || l.Id == here.InsideLocationId || l.InsideLocationId == scene.LocationId
                            || here.InsideLocationId is { } outer && l.InsideLocationId == outer)
                .Select(l => l.Id)
                .ToHashSet();
            var kinds = graph.Objects.ToDictionary(o => o.Id, o => o.Kind);
            var reachable = graph.Observations
                .Where(o => o.Edge == ObservationEdge.At && o.ObjectId is { } id && o.PlaceLocationId is { } place
                            && (place == scene.LocationId || around.Contains(place) && kinds.GetValueOrDefault(id) == ObjectKind.Fixture))
                .Select(o => o.ObjectId!.Value)
                .ToHashSet();
            var cast = scene.Cast ?? new HashSet<Guid>();
            foreach (var (characterId, seen) in WorldModelPrompt.LastSeen(graph, scene.Slot.Start, earlier).Where(p => cast.Contains(p.CharacterId)))
            {
                reachable.UnionWith(facts.PutDownBy(characterId, seen.SceneId, scene.Slot.Start).Select(f => f.ObjectId!.Value));
            }

            AppliedScene applied;
            await applying.WaitAsync(ct);
            try
            {
                applied = await InScopeAsync(userId, (sp, c) => sp.GetRequiredService<ObservationApplier>().ApplyAsync(story, scene, read.Reading, reachable, c), ct);
            }
            finally
            {
                applying.Release();
            }

            var reply = read.Reply;
            await ledger.FinishStepAsync(
                buildId, step, userId,
                new StepOutcome(reply.ModelId, reply.InputTokens, reply.OutputTokens, reply.Cost, read.Prompt, reply.Text,
                    read.Reading.Flashback == true ? $"flashback; {applied.Summary}" : applied.Summary),
                sceneDone: true, ct);
            logger.LogInformation("Build {BuildId} read scene {SceneId}: {Summary}", buildId, scene.Id, applied.Summary);
        }
        catch (Exception e)
        {
            await TryFailStepAsync(step, userId, e);
            throw;
        }
    }

    /// <summary>The story's close: connections between scenes, rooms nested with the rooms they open onto, then nodes nothing refers to any more.</summary>
    private async Task<BuildCounts> FinishAsync(Guid buildId, string userId, StorySnapshot story, int sequence, CancellationToken ct)
    {
        var step = await ledger.BeginStepAsync(buildId, userId, sequence, WorldModelStepKind.Finish, null, "Finish", ct);
        try
        {
            var (counts, summary) = await InScopeAsync(userId, async (sp, c) =>
            {
                var loader = sp.GetRequiredService<StoryLoader>();
                var unitOfWorkFactory = sp.GetRequiredService<IUnitOfWorkFactory>();
                var observations = sp.GetRequiredService<ILocationObservationRepository>();
                var observationQueries = sp.GetRequiredService<IQueryFactory<LocationObservation, ILocationObservationQuery>>();
                var parts = sp.GetRequiredService<ILocationPartRepository>();
                var partQueries = sp.GetRequiredService<IQueryFactory<LocationPart, ILocationPartQuery>>();
                var objects = sp.GetRequiredService<ILocationObjectRepository>();
                var objectQueries = sp.GetRequiredService<IQueryFactory<LocationObject, ILocationObjectQuery>>();

                using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(FinishAsync));
                await observations.DeleteByQueryAsync(observationQueries.Create().InStory(story.StoryId).OfOrigin(ObservationOrigin.Rule), c);
                var graph = await loader.LoadGraphAsync(story.StoryId, c);
                var scenes = story.Scenes.Select(s => s.Slot).ToList();
                var facts = WorldModelFolder.Fold(graph.Observations, scenes, graph.Objects.ToDictionary(o => o.Id, o => o.Kind));
                var followOn = story.Scenes.Where(s => SceneContinuity.FollowsOn(s.TimeOfDay)).Select(s => s.Id).ToHashSet();
                var connections = CrossSceneConnections.Find(graph.Observations, scenes, facts, followOn);
                foreach (var connection in connections)
                {
                    await observations.AddAsync(new LocationObservation
                    {
                        Id = Guid.NewGuid(),
                        SceneElementId = connection.SceneElementId,
                        Edge = ObservationEdge.Connects,
                        PlaceLocationId = connection.From.LocationId,
                        PlacePartId = connection.From.PartId,
                        OtherLocationId = connection.To.LocationId,
                        OtherPartId = connection.To.PartId,
                        Basis = ObservationBasis.Implied,
                        Origin = ObservationOrigin.Rule,
                    }, c);
                }

                var locations = sp.GetRequiredService<ILocationRepository>();
                var locationRows = await locations.GetByQueryAsync(sp.GetRequiredService<IQueryFactory<Location, ILocationQuery>>().Create().InStory(story.StoryId), c);
                var nested = LocationNesting.Spread(locationRows, graph.Observations);
                await unitOfWork.CommitAsync(c);

                using var sweep = unitOfWorkFactory.CreateReadWrite(nameof(FinishAsync));
                var sweptParts = await parts.DeleteByQueryAsync(partQueries.Create().InStory(story.StoryId).OfOrigin(NodeOrigin.Extracted).Unobserved(), c);
                var sweptObjects = await objects.DeleteByQueryAsync(objectQueries.Create().InStory(story.StoryId).Unobserved(), c);
                await sweep.CommitAsync(c);

                var after = await loader.LoadGraphAsync(story.StoryId, c);
                var totals = new BuildCounts(after.Parts.Count, after.Objects.Count, after.Observations.Count);
                var line = $"{connections.Count} {(connections.Count == 1 ? "connection" : "connections")} between scenes, "
                           + $"{nested} {(nested == 1 ? "location" : "locations")} nested by connection, "
                           + $"{facts.Conflicts.Count} {(facts.Conflicts.Count == 1 ? "conflict" : "conflicts")}; "
                           + $"swept {sweptParts} {(sweptParts == 1 ? "part" : "parts")} and {sweptObjects} {(sweptObjects == 1 ? "object" : "objects")}; "
                           + $"{totals.Parts} parts, {totals.Objects} objects, {totals.Observations} observations";
                return (totals, line);
            }, ct);

            await ledger.FinishStepAsync(buildId, step, userId, new StepOutcome(null, 0, 0, 0, null, null, summary), sceneDone: false, ct);
            return counts;
        }
        catch (Exception e)
        {
            await TryFailStepAsync(step, userId, e);
            throw;
        }
    }

    /// <summary>
    /// What each location the story goes to is and holds (design doc §7), inferred from the whole
    /// story: an outer place first, so the places inside it can agree with it. A location whose
    /// description fails keeps the one it had and the build still finishes.
    /// </summary>
    private async Task DescribeLocationsAsync(Guid buildId, string userId, StorySnapshot story, Func<int> nextSequence, CancellationToken ct)
    {
        var graph = await InScopeAsync(userId, (sp, c) => sp.GetRequiredService<StoryLoader>().LoadGraphAsync(story.StoryId, c), ct);
        var facts = WorldModelFolder.Fold(graph.Observations, story.Scenes.Select(s => s.Slot).ToList(), graph.Objects.ToDictionary(o => o.Id, o => o.Kind));
        var visited = graph.Locations.Where(l => LocationDescriptionPrompt.ScenesAt(story, graph, l.Id).Count > 0).ToDictionary(l => l.Id);
        var described = new ConcurrentDictionary<Guid, string>();
        using var describing = new SemaphoreSlim(Math.Max(1, options.Value.LocationConcurrency));
        foreach (var level in visited.Values.GroupBy(l => Depth(l, visited)).OrderBy(g => g.Key))
        {
            await Task.WhenAll(level.Select(async location =>
            {
                await describing.WaitAsync(ct);
                try
                {
                    await DescribeAsync(location);
                }
                finally
                {
                    describing.Release();
                }
            }));
        }

        async Task DescribeAsync(LocationInfo location)
        {
            var step = await ledger.BeginStepAsync(buildId, userId, nextSequence(), WorldModelStepKind.DescribeLocation, null, location.Name, ct);
            try
            {
                (string, string)? outer = location.InsideLocationId is { } outerId && visited.TryGetValue(outerId, out var o)
                                          && described.TryGetValue(outerId, out var d)
                    ? (o.Name, d)
                    : null;
                var read = await describer.DescribeAsync(story, graph, facts, location, outer, ct);
                var description = read.Description.Description?.Trim() is { Length: > 0 } text
                    ? text
                    : throw new InvalidOperationException("The reply gave no description.");
                var contents = read.Description.Contents?.Trim() is { Length: > 0 } inside ? inside : null;
                await InScopeAsync(userId, async (sp, c) =>
                {
                    var locations = sp.GetRequiredService<ILocationRepository>();
                    using var unitOfWork = sp.GetRequiredService<IUnitOfWorkFactory>().CreateReadWrite(nameof(DescribeLocationsAsync));
                    var row = await locations.GetFirstByQueryAsync(sp.GetRequiredService<IQueryFactory<Location, ILocationQuery>>().Create().WithId(location.Id), c);
                    if (row is not null)
                    {
                        row.Description = description;
                        row.Contents = contents;
                        await unitOfWork.CommitAsync(c);
                    }

                    return row is not null;
                }, ct);
                described[location.Id] = description;

                var reply = read.Reply;
                var summary = $"{Words(description)} words of description, {Words(contents)} of contents"
                              + (outer is { } around ? $"; inside {around.Item1}" : "");
                await ledger.FinishStepAsync(
                    buildId, step, userId,
                    new StepOutcome(reply.ModelId, reply.InputTokens, reply.OutputTokens, reply.Cost, read.Prompt, reply.Text, summary),
                    sceneDone: false, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Build {BuildId} couldn't describe location {LocationId}", buildId, location.Id);
                await TryFailStepAsync(step, userId, e);
            }
            catch (Exception e)
            {
                await TryFailStepAsync(step, userId, e);
                throw;
            }
        }
    }

    /// <summary>How many visited places it is inside; a loop of nestings counts as none.</summary>
    private static int Depth(LocationInfo location, IReadOnlyDictionary<Guid, LocationInfo> visited)
    {
        var depth = 0;
        var seen = new HashSet<Guid> { location.Id };
        for (var at = location; at.InsideLocationId is { } outer && visited.TryGetValue(outer, out var next) && seen.Add(outer); at = next)
        {
            depth++;
        }

        return depth;
    }

    private static int Words(string? text) => text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length ?? 0;

    private async Task TryFailStepAsync(Guid step, string userId, Exception e)
    {
        try
        {
            await ledger.FailStepAsync(step, userId, e is OperationCanceledException ? "Cancelled." : e.Message, CancellationToken.None);
        }
        catch (Exception failure)
        {
            logger.LogWarning(failure, "Step {StepId} could not be marked failed", step);
        }
    }

    private async Task<T> InScopeAsync<T>(string userId, Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(userId);
        return await work(scope.ServiceProvider, ct);
    }
}
