using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Data.Entities;

namespace ProtoFast.Segmentation.Routing;

/// <summary>One row for the <c>model_calls</c> audit table (plan §24.3).</summary>
public sealed record ModelCallRecord(
    string RunId,
    PipelinePhase Phase,
    AgentRole Role,
    string? Unit,
    string Provider,
    string Model,
    string LimitPool,
    string PromptVersion,
    Sensitivity Sensitivity,
    int InputTokens,
    int OutputTokens,
    int CachedInputTokens,
    decimal CostUsd,
    int LatencyMs,
    string Outcome,
    int Attempt,
    bool Batched);

public interface IModelCallLedger
{
    Task RecordAsync(ModelCallRecord record, CancellationToken ct = default);
}

/// <summary>
/// Writes the audit row and keeps the run's cost total current.
///
/// <para>It resolves its own scope rather than taking a <see cref="SegmentationDbContext"/>: the
/// routing client is a singleton shared by every concurrent call, and a shared context is neither
/// thread-safe nor able to write a row for a call whose surrounding unit of work has already been
/// disposed.</para>
/// </summary>
public sealed class ModelCallLedger(IServiceScopeFactory scopes, ILogger<ModelCallLedger> logger) : IModelCallLedger
{
    public async Task RecordAsync(ModelCallRecord record, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

            db.ModelCalls.Add(new ModelCall
            {
                RunId = record.RunId,
                Phase = record.Phase,
                Role = record.Role,
                Unit = record.Unit,
                Provider = record.Provider,
                Model = record.Model,
                LimitPool = record.LimitPool,
                PromptVersion = record.PromptVersion,
                Sensitivity = record.Sensitivity,
                InputTokens = record.InputTokens,
                OutputTokens = record.OutputTokens,
                CachedInputTokens = record.CachedInputTokens,
                CostUsd = record.CostUsd,
                LatencyMs = record.LatencyMs,
                Outcome = record.Outcome,
                Attempt = record.Attempt,
                Batched = record.Batched,
                At = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync(ct);

            if (record.CostUsd > 0)
            {
                // A SQL update rather than load-modify-save: concurrent calls on the same run would
                // otherwise lose each other's increments, and the total is what a cost ceiling and
                // a bill are read from.
                await db.Runs
                    .Where(r => r.RunId == record.RunId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(r => r.CostUsd, r => r.CostUsd + record.CostUsd),
                        ct);
            }
        }
        catch (Exception ex)
        {
            // A ledger write must never take down a call that already succeeded and was already
            // paid for — but losing the row silently would break the audit, so it is logged loudly.
            logger.LogError(
                ex, "Failed to record model call for run {RunId} ({Provider}/{Model}, {Outcome})",
                record.RunId, record.Provider, record.Model, record.Outcome);
        }
    }
}
