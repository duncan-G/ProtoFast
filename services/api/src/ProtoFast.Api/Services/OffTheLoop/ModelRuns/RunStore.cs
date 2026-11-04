using Google.Protobuf;
using Npgsql;
using NpgsqlTypes;
using OpenTelemetry;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Entities;

namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>
/// The worker's and the watchers' side of <c>loop.model_runs</c>. The worker works across users, so
/// it bypasses the user filter with plain SQL on its own connections, which also keeps progress
/// writes off the DbContext a running call is using. Every write after the claim only touches a run
/// that is still running, so an owner's cancel or dismissal is never overwritten.
/// </summary>
public sealed class RunStore(
    [FromKeyedServices(RunStore.DataSourceKey)] NpgsqlDataSource dataSource,
    RunOptions options)
{
    /// <summary>Its own data source, without the command logging the app's has: the worker and watchers poll.</summary>
    public const string DataSourceKey = "loop-runs";

    private static readonly string Running = nameof(ModelRunStatus.Running);

    /// <summary>
    /// The oldest queued run, or a running one whose worker stopped renewing it. A research run taken
    /// over starts again from nothing; a writing run keeps what it wrote, which can't be picked up.
    /// </summary>
    /// <param name="onlyUser">Claims only this user's runs, so tests sharing a database keep to their own.</param>
    public async Task<ClaimedRun?> ClaimAsync(CancellationToken ct, string? onlyUser = null)
    {
        using var quiet = SuppressInstrumentationScope.Begin();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var giveUp = new NpgsqlCommand(
                         """
                         UPDATE loop.model_runs
                         SET status = 'Failed', error = @error, finished_at = now(), lease_until = NULL,
                             version = version + 1, date_last_modified = now()
                         WHERE status = 'Running' AND lease_until < now() AND attempts >= @max
                         """, connection, transaction))
        {
            giveUp.Parameters.AddWithValue("error", LoopErrors.RunGaveUp);
            giveUp.Parameters.AddWithValue("max", options.MaxAttempts);
            await giveUp.ExecuteNonQueryAsync(ct);
        }

        await using var claim = new NpgsqlCommand(
            """
            WITH candidate AS (
                SELECT r.id FROM loop.model_runs r
                WHERE (r.status = 'Queued' OR (r.status = 'Running' AND r.lease_until < now()))
                  AND (@user::text IS NULL OR r.user_id = @user)
                ORDER BY r.date_created, r.id
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            )
            UPDATE loop.model_runs r
            SET status = 'Running', attempts = r.attempts + 1, lease_until = now() + @lease,
                progress = CASE WHEN r.kind = 'Write' THEN r.progress END,
                started_at = coalesce(r.started_at, now()), version = r.version + 1, date_last_modified = now()
            FROM candidate
            WHERE r.id = candidate.id
            RETURNING r.id, r.user_id, r.kind, r.label, r.work, r.progress, r.attempts, r.trace_parent
            """, connection, transaction);
        claim.Parameters.AddWithValue("lease", options.Lease);
        claim.Parameters.Add(new NpgsqlParameter("user", NpgsqlDbType.Text) { Value = (object?)onlyUser ?? DBNull.Value });

        ClaimedRun? run = null;
        await using (var reader = await claim.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                run = new ClaimedRun(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    Enum.Parse<ModelRunKind>(reader.GetString(2)),
                    reader.GetString(3),
                    RunWork.Parser.ParseFrom(reader.GetFieldValue<byte[]>(4)),
                    reader.IsDBNull(5) ? new RunProgress() : RunProgress.Parser.ParseFrom(reader.GetFieldValue<byte[]>(5)),
                    reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7));
            }
        }

        await transaction.CommitAsync(ct);
        return run;
    }

    /// <summary>Renews the lease of a run still running.</summary>
    /// <returns>False when the run was cancelled or dismissed, so it should stop.</returns>
    public async Task<bool> HeartbeatAsync(Guid id, CancellationToken ct)
    {
        using var quiet = SuppressInstrumentationScope.Begin();
        await using var command = dataSource.CreateCommand(
            "UPDATE loop.model_runs SET lease_until = now() + @lease WHERE id = @id AND status = @running");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("lease", options.Lease);
        command.Parameters.AddWithValue("running", Running);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <returns>False when the run was cancelled or dismissed, so it should stop.</returns>
    public async Task<bool> RecordProgressAsync(Guid id, RunProgress progress, CancellationToken ct)
    {
        using var quiet = SuppressInstrumentationScope.Begin();
        await using var command = dataSource.CreateCommand(
            "UPDATE loop.model_runs SET progress = @progress, version = version + 1, date_last_modified = now() WHERE id = @id AND status = @running");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.Add(new NpgsqlParameter("progress", NpgsqlDbType.Bytea) { Value = progress.ToByteArray() });
        command.Parameters.AddWithValue("running", Running);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task FinishAsync(
        Guid id, ModelRunStatus status, string? error, RunProgress progress, RunResult? result, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE loop.model_runs
            SET status = @status, error = @error, progress = @progress, result = @result,
                finished_at = now(), lease_until = NULL, version = version + 1, date_last_modified = now()
            WHERE id = @id AND status = @running
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("error", (object?)error?[..Math.Min(error.Length, OffTheLoopLimits.Line)] ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("progress", NpgsqlDbType.Bytea) { Value = progress.ToByteArray() });
        command.Parameters.Add(new NpgsqlParameter("result", NpgsqlDbType.Bytea) { Value = (object?)result?.ToByteArray() ?? DBNull.Value });
        command.Parameters.AddWithValue("running", Running);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Hands a run back to the queue when its instance shuts down. The claim still counts, so a watch
    /// sees the next one start over.
    /// </summary>
    public async Task ReleaseAsync(Guid id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE loop.model_runs
            SET status = 'Queued', lease_until = NULL, version = version + 1, date_last_modified = now()
            WHERE id = @id AND status = @running
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("running", Running);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        using var quiet = SuppressInstrumentationScope.Begin();
        await using var command = dataSource.CreateCommand(
            "DELETE FROM loop.model_runs WHERE status IN ('Succeeded', 'Failed', 'Cancelled') AND finished_at < now() - @keep");
        command.Parameters.AddWithValue("keep", options.KeepFinishedFor);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>A queued or running run of the user's stops; one that has ended is left as it is.</summary>
    public async Task CancelAsync(Guid id, string userId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE loop.model_runs
            SET status = 'Cancelled', finished_at = now(), lease_until = NULL, version = version + 1, date_last_modified = now()
            WHERE id = @id AND user_id = @user AND status IN ('Queued', 'Running')
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// One of the user's runs, for a watch: null when it is gone. Its progress and result are read
    /// only when it changed after version <paramref name="since"/>.
    /// </summary>
    public async Task<WatchedRun?> WatchAsync(Guid id, string userId, int since, CancellationToken ct)
    {
        using var quiet = SuppressInstrumentationScope.Begin();
        await using var command = dataSource.CreateCommand(
            """
            SELECT kind, label, status, error, company_id, application_id, note_id, attempts,
                   started_at, finished_at, date_created, version,
                   CASE WHEN version > @since THEN progress END,
                   CASE WHEN version > @since THEN result END
            FROM loop.model_runs
            WHERE id = @id AND user_id = @user
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("since", since);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var run = new ModelRun
        {
            Id = id,
            UserId = userId,
            Kind = Enum.Parse<ModelRunKind>(reader.GetString(0)),
            Label = reader.GetString(1),
            Work = [],
            Status = Enum.Parse<ModelRunStatus>(reader.GetString(2)),
            Error = reader.IsDBNull(3) ? null : reader.GetString(3),
            CompanyId = reader.IsDBNull(4) ? null : reader.GetGuid(4),
            ApplicationId = reader.IsDBNull(5) ? null : reader.GetGuid(5),
            NoteId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
            Attempts = reader.GetInt32(7),
            StartedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            FinishedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            DateCreated = reader.GetDateTime(10),
            Version = reader.GetInt32(11),
        };
        var changed = run.Version > since;
        return new WatchedRun(
            run,
            changed,
            changed && !reader.IsDBNull(12) ? RunProgress.Parser.ParseFrom(reader.GetFieldValue<byte[]>(12)) : new RunProgress(),
            changed && !reader.IsDBNull(13) ? RunResult.Parser.ParseFrom(reader.GetFieldValue<byte[]>(13)) : null);
    }
}
