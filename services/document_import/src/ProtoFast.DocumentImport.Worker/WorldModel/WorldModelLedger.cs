using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>
/// The build's ledger: its status, what each step did and cost. Every write is its own scope and
/// commit, as the story's owner, so steps read in parallel can record themselves side by side.
/// A build's status moves only forward: nothing replaces Finished, Failed or Cancelled.
/// </summary>
public sealed class WorldModelLedger(IServiceScopeFactory scopes, TimeProvider time)
{
    private static readonly WorldModelBuildStatus[] Claimable =
        [WorldModelBuildStatus.Queued, WorldModelBuildStatus.Running, WorldModelBuildStatus.Retrying];

    /// <summary>Marks the build running for this delivery; false when it was cancelled, finished or deleted meanwhile.</summary>
    public async Task<bool> ClaimAsync(Guid buildId, string userId, string? traceId, CancellationToken ct)
    {
        await using var ledger = Scope.Open(scopes, userId, nameof(ClaimAsync));
        var now = Now;
        var claimed = await ledger.Builds.UpdateByQueryAsync(
            ledger.BuildQueries.Create().WithId(buildId).Active(),
            set => set
                .SetProperty(b => b.Status, WorldModelBuildStatus.Running)
                .SetProperty(b => b.StartedAt, b => b.StartedAt ?? now)
                .SetProperty(b => b.Deliveries, b => b.Deliveries + 1)
                .SetProperty(b => b.TraceId, b => traceId ?? b.TraceId)
                .SetProperty(b => b.Failure, (string?)null)
                .SetProperty(b => b.Message, "Reading the story")
                .SetProperty(b => b.DateLastModified, now),
            ct);
        await ledger.CommitAsync(ct);
        return claimed == 1;
    }

    public async Task<BuildResume> ResumeAsync(Guid buildId, string userId, CancellationToken ct)
    {
        await using var ledger = Scope.Open(scopes, userId, nameof(ResumeAsync), readOnly: true);
        var steps = await ledger.Steps.GetByQueryAsync(
            ledger.StepQueries.Create().InBuild(buildId), s => new { s.Sequence, s.Kind, s.Status, s.SceneId }, ct);
        var handled = steps
            .Where(s => s.Kind == WorldModelStepKind.ReadScene && s.Status is WorldModelStepStatus.Finished or WorldModelStepStatus.Skipped && s.SceneId is not null)
            .Select(s => s.SceneId!.Value)
            .ToHashSet();
        var seeded = steps.Any(s => s.Kind == WorldModelStepKind.Seed && s.Status == WorldModelStepStatus.Finished);
        return new BuildResume(handled, steps.Count == 0 ? 1 : steps.Max(s => s.Sequence) + 1, seeded);
    }

    public async Task SetTotalAsync(Guid buildId, string userId, int scenesTotal, CancellationToken ct)
    {
        await using var ledger = Scope.Open(scopes, userId, nameof(SetTotalAsync));
        var now = Now;
        await ledger.Builds.UpdateByQueryAsync(
            ledger.BuildQueries.Create().WithId(buildId),
            set => set.SetProperty(b => b.ScenesTotal, scenesTotal).SetProperty(b => b.DateLastModified, now),
            ct);
        await ledger.CommitAsync(ct);
    }

    public async Task<Guid> BeginStepAsync(
        Guid buildId, string userId, int sequence, WorldModelStepKind kind, Guid? sceneId, string title, CancellationToken ct)
    {
        await using var ledger = Scope.Open(scopes, userId, nameof(BeginStepAsync));
        var step = new WorldModelBuildStep
        {
            Id = Guid.NewGuid(),
            BuildId = buildId,
            Sequence = sequence,
            Kind = kind,
            SceneId = sceneId,
            Title = title,
            Status = WorldModelStepStatus.Running,
            StartedAt = Now,
        };
        await ledger.Steps.AddAsync(step, ct);
        if (kind == WorldModelStepKind.ReadScene)
        {
            var now = Now;
            var message = $"Reading {title}";
            await ledger.Builds.UpdateByQueryAsync(
                ledger.BuildQueries.Create().WithId(buildId),
                set => set.SetProperty(b => b.Message, message).SetProperty(b => b.DateLastModified, now),
                ct);
        }

        await ledger.CommitAsync(ct);
        return step.Id;
    }

    /// <summary>Records the step and adds its cost to the build; a scene read also counts as a scene done.</summary>
    public async Task FinishStepAsync(Guid buildId, Guid stepId, string userId, StepOutcome outcome, bool sceneDone, CancellationToken ct)
    {
        await using var ledger = Scope.Open(scopes, userId, nameof(FinishStepAsync));
        var now = Now;
        await ledger.Steps.UpdateByQueryAsync(
            ledger.StepQueries.Create().WithId(stepId),
            set => set
                .SetProperty(s => s.Status, WorldModelStepStatus.Finished)
                .SetProperty(s => s.FinishedAt, now)
                .SetProperty(s => s.ModelId, outcome.ModelId)
                .SetProperty(s => s.InputTokens, outcome.InputTokens)
                .SetProperty(s => s.OutputTokens, outcome.OutputTokens)
                .SetProperty(s => s.Cost, outcome.Cost)
                .SetProperty(s => s.Prompt, outcome.Prompt)
                .SetProperty(s => s.Reply, outcome.Reply)
                .SetProperty(s => s.Summary, outcome.Summary),
            ct);
        var done = sceneDone ? 1 : 0;
        await ledger.Builds.UpdateByQueryAsync(
            ledger.BuildQueries.Create().WithId(buildId),
            set => set
                .SetProperty(b => b.Cost, b => b.Cost + outcome.Cost)
                .SetProperty(b => b.ScenesDone, b => b.ScenesDone + done)
                .SetProperty(b => b.DateLastModified, now),
            ct);
        await ledger.CommitAsync(ct);
    }

    public async Task SkipStepAsync(Guid buildId, Guid stepId, string userId, string reason, CancellationToken ct)
    {
        await using var ledger = Scope.Open(scopes, userId, nameof(SkipStepAsync));
        var now = Now;
        await ledger.Steps.UpdateByQueryAsync(
            ledger.StepQueries.Create().WithId(stepId),
            set => set
                .SetProperty(s => s.Status, WorldModelStepStatus.Skipped)
                .SetProperty(s => s.FinishedAt, now)
                .SetProperty(s => s.Summary, reason),
            ct);
        await ledger.Builds.UpdateByQueryAsync(
            ledger.BuildQueries.Create().WithId(buildId),
            set => set.SetProperty(b => b.ScenesDone, b => b.ScenesDone + 1).SetProperty(b => b.DateLastModified, now),
            ct);
        await ledger.CommitAsync(ct);
    }

    public async Task FailStepAsync(Guid stepId, string userId, string failure, CancellationToken ct)
    {
        await using var ledger = Scope.Open(scopes, userId, nameof(FailStepAsync));
        var now = Now;
        await ledger.Steps.UpdateByQueryAsync(
            ledger.StepQueries.Create().WithId(stepId),
            set => set
                .SetProperty(s => s.Status, WorldModelStepStatus.Failed)
                .SetProperty(s => s.FinishedAt, now)
                .SetProperty(s => s.Failure, failure),
            ct);
        await ledger.CommitAsync(ct);
    }

    /// <summary>Finishes the build unless its owner cancelled it meanwhile.</summary>
    public async Task FinishAsync(Guid buildId, string userId, BuildCounts counts, CancellationToken ct)
    {
        await using var ledger = Scope.Open(scopes, userId, nameof(FinishAsync));
        var now = Now;
        await ledger.Builds.UpdateByQueryAsync(
            ledger.BuildQueries.Create().WithId(buildId).Active(),
            set => set
                .SetProperty(b => b.Status, WorldModelBuildStatus.Finished)
                .SetProperty(b => b.FinishedAt, now)
                .SetProperty(b => b.Message, "Done")
                .SetProperty(b => b.PartCount, counts.Parts)
                .SetProperty(b => b.ObjectCount, counts.Objects)
                .SetProperty(b => b.ObservationCount, counts.Observations)
                .SetProperty(b => b.DateLastModified, now),
            ct);
        await ledger.CommitAsync(ct);
    }

    /// <summary>Retrying while the queue will deliver the request again; Failed once it will not.</summary>
    public async Task FailAsync(Guid buildId, string userId, string failure, bool final, CancellationToken ct)
    {
        await using var ledger = Scope.Open(scopes, userId, nameof(FailAsync));
        var now = Now;
        var status = final ? WorldModelBuildStatus.Failed : WorldModelBuildStatus.Retrying;
        var message = final
            ? "The world model couldn’t be built. Try again later."
            : "Something went wrong. The build will pick up where it left off shortly.";
        await ledger.Builds.UpdateByQueryAsync(
            ledger.BuildQueries.Create().WithId(buildId).Active(),
            set => set
                .SetProperty(b => b.Status, status)
                .SetProperty(b => b.FinishedAt, final ? now : null)
                .SetProperty(b => b.Failure, failure)
                .SetProperty(b => b.Message, message)
                .SetProperty(b => b.DateLastModified, now),
            ct);
        await ledger.CommitAsync(ct);
    }

    public async Task<WorldModelBuildStatus?> StatusAsync(Guid buildId, string userId, CancellationToken ct)
    {
        await using var ledger = Scope.Open(scopes, userId, nameof(StatusAsync), readOnly: true);
        var status = await ledger.Builds.GetByQueryAsync(ledger.BuildQueries.Create().WithId(buildId), b => b.Status, ct);
        return status.Count == 0 ? null : status[0];
    }

    public static bool IsClaimable(WorldModelBuildStatus status) => Claimable.Contains(status);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    private sealed class Scope(AsyncServiceScope scope, IDisposable user, IUnitOfWork unitOfWork) : IAsyncDisposable
    {
        public IWorldModelBuildRepository Builds { get; } = scope.ServiceProvider.GetRequiredService<IWorldModelBuildRepository>();

        public IQueryFactory<WorldModelBuild, IWorldModelBuildQuery> BuildQueries { get; } =
            scope.ServiceProvider.GetRequiredService<IQueryFactory<WorldModelBuild, IWorldModelBuildQuery>>();

        public IWorldModelBuildStepRepository Steps { get; } = scope.ServiceProvider.GetRequiredService<IWorldModelBuildStepRepository>();

        public IQueryFactory<WorldModelBuildStep, IWorldModelBuildStepQuery> StepQueries { get; } =
            scope.ServiceProvider.GetRequiredService<IQueryFactory<WorldModelBuildStep, IWorldModelBuildStepQuery>>();

        public static Scope Open(IServiceScopeFactory scopes, string userId, string operation, bool readOnly = false)
        {
            var scope = scopes.CreateAsyncScope();
            var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(userId);
            var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
            var unitOfWork = readOnly ? factory.CreateReadOnly(operation) : factory.CreateReadWrite(operation);
            return new Scope(scope, user, unitOfWork);
        }

        public Task CommitAsync(CancellationToken ct) => unitOfWork.CommitAsync(ct);

        public async ValueTask DisposeAsync()
        {
            unitOfWork.Dispose();
            user.Dispose();
            await scope.DisposeAsync();
        }
    }
}
