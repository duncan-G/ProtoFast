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
/// gives, then finish. Scenes in one location run in story order, each starting from the graph
/// the last one left; scenes in different locations run side by side. A delivery that follows a
/// failed one skips the scenes already read. Returns false when the build was no longer there
/// to run: cancelled, finished by another delivery, or deleted with its story.
/// </summary>
public sealed class WorldModelBuildRunner(
    IServiceScopeFactory scopes,
    WorldModelLedger ledger,
    SceneReader reader,
    IOptions<WorldModelBuildOptions> options,
    ILogger<WorldModelBuildRunner> logger)
{
    private static readonly string[] NestingSeparators = [" – ", " — ", " - ", " / "];

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

        var groups = story.Scenes
            .Where(s => s.LocationId is not null)
            .GroupBy(s => s.LocationId!.Value)
            .Select(g => g.OrderBy(s => s.Slot.Start).ToList())
            .ToList();
        using var applying = new SemaphoreSlim(1, 1);
        await Parallel.ForEachAsync(
            groups,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.LocationConcurrency), CancellationToken = ct },
            async (group, token) =>
            {
                foreach (var scene in group.Where(s => !resume.HandledScenes.Contains(s.Id)))
                {
                    await ReadSceneAsync(buildId, userId, story, scene, Interlocked.Increment(ref sequence), applying, token);
                }
            });

        await FinishAsync(buildId, userId, story, Interlocked.Increment(ref sequence), ct);
        return true;
    }

    /// <summary>Nests locations whose names say so: "HOSPITAL – TRAUMA WING" is inside "HOSPITAL" when both exist.</summary>
    private async Task SeedAsync(Guid buildId, string userId, StorySnapshot story, int sequence, CancellationToken ct)
    {
        var step = await ledger.BeginStepAsync(buildId, userId, sequence, WorldModelStepKind.Seed, null, "Seed", ct);
        try
        {
            var nested = await InScopeAsync(userId, async (sp, c) =>
            {
                var unitOfWorkFactory = sp.GetRequiredService<IUnitOfWorkFactory>();
                var locations = sp.GetRequiredService<ILocationRepository>();
                var queries = sp.GetRequiredService<IQueryFactory<Location, ILocationQuery>>();
                using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(SeedAsync));
                var rows = await locations.GetByQueryAsync(queries.Create().InStory(story.StoryId), c);
                var count = 0;
                foreach (var location in rows.Where(l => l.InsideLocationId is null))
                {
                    var separator = NestingSeparators.Select(s => location.Name.LastIndexOf(s, StringComparison.Ordinal)).Where(i => i > 0).DefaultIfEmpty(-1).Max();
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var outerName = location.Name[..separator].Trim();
                    var outer = rows.FirstOrDefault(l => l.Id != location.Id && string.Equals(l.Name, outerName, StringComparison.OrdinalIgnoreCase));
                    if (outer is not null)
                    {
                        location.InsideLocationId = outer.Id;
                        count++;
                    }
                }

                await unitOfWork.CommitAsync(c);
                return count;
            }, ct);

            var summary = $"{story.Locations.Count} locations, {story.Characters.Count} characters, {story.Scenes.Count} scenes; "
                          + $"{nested} {(nested == 1 ? "location" : "locations")} nested by name";
            await ledger.FinishStepAsync(buildId, step, userId, new StepOutcome(null, 0, 0, 0, null, null, summary), sceneDone: false, ct);
        }
        catch (Exception e)
        {
            await TryFailStepAsync(step, userId, e);
            throw;
        }
    }

    private async Task ReadSceneAsync(
        Guid buildId, string userId, StorySnapshot story, SceneInfo scene, int sequence, SemaphoreSlim applying, CancellationToken ct)
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
            var read = await reader.ReadAsync(story, scene, graph, facts, ct);

            AppliedScene applied;
            await applying.WaitAsync(ct);
            try
            {
                applied = await InScopeAsync(userId, (sp, c) => sp.GetRequiredService<ObservationApplier>().ApplyAsync(story, scene, read.Reading, c), ct);
            }
            finally
            {
                applying.Release();
            }

            var reply = read.Reply;
            await ledger.FinishStepAsync(
                buildId, step, userId,
                new StepOutcome(reply.ModelId, reply.InputTokens, reply.OutputTokens, reply.Cost, read.Prompt, reply.Text, applied.Summary),
                sceneDone: true, ct);
            logger.LogInformation("Build {BuildId} read scene {SceneId}: {Summary}", buildId, scene.Id, applied.Summary);
        }
        catch (Exception e)
        {
            await TryFailStepAsync(step, userId, e);
            throw;
        }
    }

    /// <summary>The story's close: connections between scenes, then nodes nothing refers to any more.</summary>
    private async Task FinishAsync(Guid buildId, string userId, StorySnapshot story, int sequence, CancellationToken ct)
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
                var connections = CrossSceneConnections.Find(graph.Observations, scenes, facts);
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
                        ViaObjectId = connection.ViaObjectId,
                        Basis = ObservationBasis.Implied,
                        Origin = ObservationOrigin.Rule,
                    }, c);
                }

                await unitOfWork.CommitAsync(c);

                using var sweep = unitOfWorkFactory.CreateReadWrite(nameof(FinishAsync));
                var sweptParts = await parts.DeleteByQueryAsync(partQueries.Create().InStory(story.StoryId).OfOrigin(NodeOrigin.Extracted).Unobserved(), c);
                var sweptObjects = await objects.DeleteByQueryAsync(objectQueries.Create().InStory(story.StoryId).Unobserved(), c);
                await sweep.CommitAsync(c);

                var after = await loader.LoadGraphAsync(story.StoryId, c);
                var totals = new BuildCounts(after.Parts.Count, after.Objects.Count, after.Observations.Count);
                var line = $"{connections.Count} {(connections.Count == 1 ? "connection" : "connections")} between scenes, "
                           + $"{facts.Conflicts.Count} {(facts.Conflicts.Count == 1 ? "conflict" : "conflicts")}; "
                           + $"swept {sweptParts} {(sweptParts == 1 ? "part" : "parts")} and {sweptObjects} {(sweptObjects == 1 ? "object" : "objects")}; "
                           + $"{totals.Parts} parts, {totals.Objects} objects, {totals.Observations} observations";
                return (totals, line);
            }, ct);

            await ledger.FinishStepAsync(buildId, step, userId, new StepOutcome(null, 0, 0, 0, null, null, summary), sceneDone: false, ct);
            await ledger.FinishAsync(buildId, userId, counts, ct);
        }
        catch (Exception e)
        {
            await TryFailStepAsync(step, userId, e);
            throw;
        }
    }

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
