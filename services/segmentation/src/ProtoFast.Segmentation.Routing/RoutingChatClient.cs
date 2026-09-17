using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Routing.Budgets;
using ProtoFast.Segmentation.Routing.Providers;

namespace ProtoFast.Segmentation.Routing;

/// <summary>
/// Chooses the model, reserves the budget, makes the call, retries it, records it (plan §14).
///
/// <para>The ordering of the filter in <see cref="SelectCandidatesAsync"/> is the security
/// boundary: sensitivity is checked first and unconditionally, before qualification, headroom or
/// cost can influence anything. A document marked <c>Restricted</c> cannot reach a provider that
/// is not approved for it, no matter how much cheaper or freer that provider is.</para>
/// </summary>
public sealed class RoutingChatClient(
    IModelRegistry registry,
    IBudgetLedger budgets,
    IProviderClientCache clients,
    IModelCallLedger ledger,
    IOptions<RoutingOptions> options,
    ILogger<RoutingChatClient> logger,
    TimeProvider timeProvider) : IRoutingChatClient
{
    /// <summary>Named so the collector's existing OTLP pipeline picks these spans up unchanged.</summary>
    public static readonly ActivitySource ActivitySource = new("ProtoFast.Segmentation.Routing");

    private readonly RoutingOptions _options = options.Value;

    public async Task<RoutedResponse> GetResponseAsync(
        IReadOnlyList<ChatMessage> messages,
        RoutingContext context,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("llm.call", ActivityKind.Client);
        activity?.SetTag("run.id", context.RunId);
        activity?.SetTag("phase", context.Phase.ToString());
        activity?.SetTag("agent.role", context.Role.ToString());

        var deadline = timeProvider.GetUtcNow().Add(_options.MaxRoutingWait);
        var candidates = await SelectCandidatesAsync(context, ct);

        if (candidates.Count == 0)
        {
            throw new NoEligibleModelException(
                $"No model is qualified for {context.Role} at tier {context.Tier} and sensitivity " +
                $"{context.Sensitivity} with prompt version {context.PromptVersion}. " +
                "The tier is never downgraded to work around this — qualify a model instead.");
        }

        var attempt = 0;
        Exception? lastFailure = null;

        while (timeProvider.GetUtcNow() < deadline)
        {
            foreach (var candidate in candidates)
            {
                var reservation = await ReserveAsync(candidate, context, ct);
                if (reservation is null)
                {
                    continue;
                }

                try
                {
                    return await CallAsync(candidate, reservation, messages, context, ++attempt, activity, ct);
                }
                catch (ProviderException ex) when (ex.IsRetryable)
                {
                    lastFailure = ex;
                    await budgets.ReleaseAsync(reservation, ct);
                    await budgets.RecordOutcomeAsync(
                        candidate.Model.LimitPool, success: false,
                        rateLimited: ex.Kind == ProviderErrorKind.RateLimited, latencyMs: 0, ct);

                    if (attempt >= _options.Resilience.MaxAttempts)
                    {
                        break;
                    }

                    await DelayAsync(ex.RetryAfter ?? Backoff(attempt), ct);
                }
                catch
                {
                    // Non-retryable: a bad request, an auth failure, a content-policy refusal or a
                    // context overflow. Releasing the reservation matters even here — the budget
                    // was never spent, and holding it would throttle calls that could succeed.
                    await budgets.ReleaseAsync(reservation, ct);
                    await budgets.RecordOutcomeAsync(candidate.Model.LimitPool, false, false, 0, ct);
                    throw;
                }
            }

            if (attempt >= _options.Resilience.MaxAttempts)
            {
                break;
            }

            // Nothing had headroom. Wait for a bucket to drain rather than downgrading the tier.
            await DelayAsync(TimeSpan.FromSeconds(2), ct);
        }

        throw new NoEligibleModelException(
            $"No eligible model had headroom for {context.Role} within {_options.MaxRoutingWait}. " +
            $"Last failure: {lastFailure?.Message ?? "none"}. The run returns to the queue.");
    }

    public async Task<IReadOnlyList<ModelStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        var statuses = new List<ModelStatus>(registry.Models.Count);

        foreach (var model in registry.Models)
        {
            var headroom = await budgets.GetHeadroomAsync(model.LimitPool, ct);
            var roles = new List<string>();

            foreach (var role in Enum.GetValues<AgentRole>())
            {
                if (model.PresumedQualifiedRoles.Contains(role))
                {
                    roles.Add(role.ToString());
                }
            }

            statuses.Add(new ModelStatus(
                model.Key, model.Provider, model.Tier.ToString(), roles,
                headroom.Fraction, headroom.CircuitOpen));
        }

        return statuses;
    }

    private sealed record Candidate(ModelDescriptor Model, double Score, bool IsPinned);

    /// <summary>
    /// The filter and score of plan §14.5, in that order. Everything before the score is a hard
    /// constraint; the score only ranks what is already allowed.
    /// </summary>
    private async Task<List<Candidate>> SelectCandidatesAsync(RoutingContext context, CancellationToken ct)
    {
        var eligible = new List<Candidate>();
        var pinned = context.PinnedModelKey is null ? null : registry.Find(context.PinnedModelKey);

        foreach (var model in registry.Models)
        {
            // 1. Sensitivity. First, and never relaxed.
            if (!model.AllowedSensitivity.Contains(context.Sensitivity))
            {
                continue;
            }

            // 2. Tier. Also never relaxed — a small model on a structurer's job is a worse answer,
            //    not a cheaper one.
            if (model.Tier != context.Tier)
            {
                continue;
            }

            // 3. Qualified for this role at THIS prompt version.
            if (!await registry.IsQualifiedAsync(model, context.Role, context.PromptVersion, ct))
            {
                continue;
            }

            // 4. The window has to fit, with room for the answer.
            if (context.EstimatedInputTokens + context.MaxOutputTokens > model.ContextTokens)
            {
                continue;
            }

            // 5. A provider with no key configured is not a candidate.
            if (clients.Get(model) is null)
            {
                continue;
            }

            // 6. Circuit closed.
            var headroom = await budgets.GetHeadroomAsync(model.LimitPool, ct);
            if (headroom.CircuitOpen)
            {
                continue;
            }

            var statistics = await budgets.GetStatisticsAsync(model.LimitPool, ct);
            eligible.Add(new Candidate(model, Score(model, headroom, statistics), model.Key == pinned?.Key));
        }

        // 7. AvoidProvider is a preference, not a filter: drop it only if it would empty the list.
        if (context.AvoidProvider is { } avoid)
        {
            var others = eligible.Where(c => c.Model.Provider != avoid).ToList();
            if (others.Count > 0)
            {
                eligible = others;
            }
        }

        // 8. Stickiness: the pinned model goes first when it is still eligible, so a document's
        //    labels stay consistent across windows (plan §14.6).
        return [.. eligible.OrderByDescending(c => c.IsPinned).ThenByDescending(c => c.Score)];
    }

    private double Score(ModelDescriptor model, PoolHeadroom headroom, PoolStatistics statistics)
    {
        var weights = _options.Weights;
        var maxCost = registry.Models.Max(m => m.CostPerMTok);
        var normalizedCost = maxCost == 0 ? 0 : (double)(model.CostPerMTok / maxCost);

        // Latency is normalized against a 60s ceiling: past a minute the differences stop mattering
        // to a batch pipeline, and an unbounded term would let one slow sample dominate the score.
        var normalizedLatency = Math.Clamp(statistics.P95LatencyMs / 60_000.0, 0, 1);

        return weights.Headroom * headroom.Fraction
            + weights.Cost * (1 - normalizedCost)
            + weights.Errors * (1 - statistics.ErrorRate)
            + weights.Latency * (1 - normalizedLatency);
    }

    private async Task<BudgetReservation?> ReserveAsync(Candidate candidate, RoutingContext context, CancellationToken ct)
    {
        var estimatedCost = EstimateCost(candidate.Model, context.EstimatedInputTokens, context.MaxOutputTokens);

        return await budgets.TryReserveAsync(
            new BudgetRequest(
                candidate.Model.LimitPool,
                context.EstimatedInputTokens,
                Math.Min(context.MaxOutputTokens, candidate.Model.MaxOutputTokens),
                estimatedCost),
            ct);
    }

    private async Task<RoutedResponse> CallAsync(
        Candidate candidate,
        BudgetReservation reservation,
        IReadOnlyList<ChatMessage> messages,
        RoutingContext context,
        int attempt,
        Activity? activity,
        CancellationToken ct)
    {
        var client = clients.Get(candidate.Model)
            ?? throw new NoEligibleModelException($"No client for {candidate.Model.Key}.");

        activity?.SetTag("llm.provider", candidate.Model.Provider);
        activity?.SetTag("llm.model", candidate.Model.ModelName);
        activity?.SetTag("routing.score", candidate.Score);
        activity?.SetTag("routing.switch", !candidate.IsPinned && context.PinnedModelKey is not null);

        var stopwatch = Stopwatch.StartNew();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.Resilience.Timeout);

        // The scope has to be open BEFORE the call so it flows into the SDK's HTTP continuation;
        // opening it afterwards would capture nothing.
        using var rateLimits = RateLimitHeaderHandler.Capture();

        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(
                messages,
                new ChatOptions
                {
                    ModelId = candidate.Model.ModelName,
                    MaxOutputTokens = Math.Min(context.MaxOutputTokens, candidate.Model.MaxOutputTokens),
                    // Zero for labeling and structure; the augmentation types that want variety
                    // override it when they build their own options (plan §10.4).
                    Temperature = 0,
                    ResponseFormat = candidate.Model.ParsedCapabilities.HasFlag(ModelCapabilities.JsonMode)
                        ? ChatResponseFormat.Json
                        : null,
                },
                timeout.Token);
        }
        catch (Exception ex)
        {
            var provider = ProviderException.From(ex, candidate.Model.Provider);

            // The ledger row is written for failures too. A call that was billed and then failed
            // is exactly the call an audit needs to see, and the provider charges for it either way.
            await ledger.RecordAsync(new ModelCallRecord(
                context.RunId, context.Phase, context.Role, context.Unit,
                candidate.Model.Provider, candidate.Model.ModelName, candidate.Model.LimitPool,
                context.PromptVersion, context.Sensitivity,
                0, 0, 0, 0m, (int)stopwatch.ElapsedMilliseconds,
                provider.Kind.ToString(), attempt, Batched: false), CancellationToken.None);

            throw provider;
        }

        stopwatch.Stop();

        if (context.PinnedModelKey is { } pinned && !candidate.IsPinned)
        {
            // A switch away from the pinned model is the thing to look for when a document shows
            // boundary errors at one point and nowhere else (plan §14.6).
            logger.LogInformation(
                "Run {RunId} phase {Phase}: switched from pinned {Pinned} to {Model}; windows after "
                + "this point are candidates for a seam-consistency check.",
                context.RunId, context.Phase, pinned, candidate.Model.Key);
        }

        var usage = CallUsage.From(response.Usage);
        var cost = ActualCost(candidate.Model, usage);

        await budgets.ReconcileAsync(reservation, usage.InputTokens, usage.OutputTokens, cost, ct);
        await budgets.RecordOutcomeAsync(candidate.Model.LimitPool, true, false, (int)stopwatch.ElapsedMilliseconds, ct);

        if (rateLimits.Snapshot is { HasAnything: true } snapshot)
        {
            await budgets.ObserveLimitsAsync(
                candidate.Model.LimitPool, snapshot.RemainingRequests, snapshot.RemainingTokens, snapshot.ResetAfter, ct);
        }

        activity?.SetTag("llm.tokens.input", usage.InputTokens);
        activity?.SetTag("llm.tokens.output", usage.OutputTokens);
        activity?.SetTag("llm.cache.hit", usage.CachedInputTokens > 0);

        await ledger.RecordAsync(new ModelCallRecord(
            context.RunId, context.Phase, context.Role, context.Unit,
            candidate.Model.Provider, candidate.Model.ModelName, candidate.Model.LimitPool,
            context.PromptVersion, context.Sensitivity,
            usage.InputTokens, usage.OutputTokens, usage.CachedInputTokens, cost,
            (int)stopwatch.ElapsedMilliseconds, "ok", attempt, Batched: false), ct);

        return new RoutedResponse(
            response.Text ?? string.Empty,
            new RoutingDecision(
                candidate.Model, candidate.Score, candidate.IsPinned,
                SwitchedFromPinned: context.PinnedModelKey is not null && !candidate.IsPinned),
            usage.InputTokens,
            usage.OutputTokens,
            usage.CachedInputTokens,
            cost,
            (int)stopwatch.ElapsedMilliseconds);
    }

    internal static decimal EstimateCost(ModelDescriptor model, int inputTokens, int maxOutputTokens) =>
        (inputTokens * model.InputCostPerMTok + maxOutputTokens * model.OutputCostPerMTok) / 1_000_000m;

    private static decimal ActualCost(ModelDescriptor model, CallUsage usage) =>
        (usage.InputTokens * model.InputCostPerMTok + usage.OutputTokens * model.OutputCostPerMTok) / 1_000_000m;

    /// <summary>Exponential backoff with full jitter — synchronized retries are what make a 429 storm.</summary>
    private TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(
            Random.Shared.NextDouble() * _options.Resilience.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

    private Task DelayAsync(TimeSpan delay, CancellationToken ct) =>
        Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1), timeProvider, ct);
}
