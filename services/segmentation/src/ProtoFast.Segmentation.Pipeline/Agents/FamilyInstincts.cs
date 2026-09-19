using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Data.Entities;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>
/// Learned per-family guidance (plan §15.4).
///
/// <para>Two constraints define this feature, and both are safety properties rather than quality
/// ones. Instincts derive <em>only</em> from reviewer and human corrections, never from document
/// text — otherwise a document could teach the pipeline to misread the next one (plan §24.1). And
/// they are advisory: an instinct reaches a prompt and nothing else, so it can never override a
/// trusted boundary or a validation check.</para>
/// </summary>
public sealed class FamilyInstincts(IServiceScopeFactory scopes, TimeProvider clock)
{
    /// <summary>Below this, an instinct is a hypothesis rather than guidance (plan §15.4).</summary>
    public const double MinimumConfidence = 0.7;

    /// <summary>At most this many reach a prompt; the highest-confidence ones win.</summary>
    public const int MaxPerPrompt = 6;

    /// <summary>Confidence lost per 30 days without a confirmation.</summary>
    public const double DecayPer30Days = 0.05;

    public Task<IReadOnlyList<string>> ForFamilyAsync(string family, CancellationToken ct = default) =>
        ForScopeAsync(family, axis: null, scope: null, ct);

    /// <summary>
    /// The guidance one agent may see (scene plan §7.1).
    ///
    /// <para><see cref="InstinctScope"/> is what keeps the six-per-prompt budget spent on relevant
    /// guidance: without it every consumer would draw from one pool and see instincts meant for
    /// another agent. <see cref="FamilyAxis"/> keeps a production instinct — "pages 1–4 are front
    /// matter in this publisher's template" — from being offered as if it were a fact about the kind
    /// of work.</para>
    ///
    /// <para>Null on either parameter means "any", which is what <see cref="ForFamilyAsync"/> asks
    /// for: an instinct captured before the axis existed still applies to the agent it was captured
    /// from.</para>
    /// </summary>
    public async Task<IReadOnlyList<string>> ForScopeAsync(
        string family,
        FamilyAxis? axis,
        InstinctScope? scope,
        CancellationToken ct = default)
    {
        await using var dbScope = scopes.CreateAsyncScope();
        var db = dbScope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var query = db.FamilyInstincts
            .AsNoTracking()
            .Where(i => i.Family == family && i.PromotedAt == null);

        if (axis is { } requiredAxis)
        {
            query = query.Where(i => i.Axis == requiredAxis);
        }

        if (scope is { } requiredScope)
        {
            query = query.Where(i => i.Scope == requiredScope);
        }

        var rows = await query.ToListAsync(ct);

        var now = clock.GetUtcNow();

        return
        [
            .. rows
                .Select(i => (Instinct: i, Confidence: Decayed(i, now)))
                .Where(x => x.Confidence >= MinimumConfidence)
                .OrderByDescending(x => x.Confidence)
                .Take(MaxPerPrompt)
                .Select(x => $"{x.Instinct.Pattern} — {x.Instinct.Guidance}"),
        ];
    }

    /// <summary>
    /// Records a correction a person or a reviewer made. Repeated corrections of the same pattern
    /// raise its confidence; the first sighting is stored well below the injection threshold, so
    /// one reviewer's one-off opinion never reaches a prompt.
    /// </summary>
    public Task ConfirmAsync(string family, string pattern, string guidance, CancellationToken ct = default) =>
        ConfirmAsync(family, FamilyAxis.Composition, InstinctScope.Structure, pattern, guidance, ct);

    /// <inheritdoc cref="ConfirmAsync(string, string, string, CancellationToken)"/>
    public async Task ConfirmAsync(
        string family,
        FamilyAxis axis,
        InstinctScope instinctScope,
        string pattern,
        string guidance,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var now = clock.GetUtcNow();

        // Keyed by (family, axis, scope, pattern): the same words can be a true production instinct
        // and a false composition one, and collapsing them would let one reviewer's correction about
        // a publisher's template reach an agent reasoning about the kind of work.
        var row = await db.FamilyInstincts
            .FirstOrDefaultAsync(
                i => i.Family == family && i.Axis == axis && i.Scope == instinctScope && i.Pattern == pattern,
                ct);

        if (row is null)
        {
            db.FamilyInstincts.Add(new FamilyInstinct
            {
                Family = family,
                Axis = axis,
                Scope = instinctScope,
                Pattern = pattern,
                Guidance = guidance,
                Confidence = 0.3,
                Confirmations = 1,
                CreatedAt = now,
                LastSeen = now,
            });
        }
        else
        {
            // Diminishing returns: each confirmation closes a fifth of the remaining gap to 1.0, so
            // confidence approaches certainty without a handful of sightings ever reaching it.
            row.Confidence = Math.Min(1.0, Decayed(row, now) + (1 - Decayed(row, now)) * 0.2);
            row.Confirmations++;
            row.Guidance = guidance;
            row.LastSeen = now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Confidence with unused time subtracted. Decay is computed on read rather than written by a
    /// background job: there is no scheduler on Host B, and a value derived from
    /// <c>LastSeen</c> is correct whenever it is asked for.
    /// </summary>
    internal static double Decayed(FamilyInstinct instinct, DateTimeOffset now)
    {
        var periods = (now - instinct.LastSeen).TotalDays / 30;
        return Math.Max(0, instinct.Confidence - periods * DecayPer30Days);
    }
}
