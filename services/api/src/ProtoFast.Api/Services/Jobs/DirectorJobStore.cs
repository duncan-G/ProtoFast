using Google.Protobuf;
using Npgsql;
using NpgsqlTypes;
using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Api.Services.Jobs;

/// <summary>
/// The worker's side of <c>plot.director_jobs</c>. It works across users, so it bypasses the user
/// filter with plain SQL on its own connections, which also keeps heartbeats off the DbContext a
/// running step is using. Every write after the claim only touches a job that is still running, so
/// an owner's cancel is never overwritten.
/// </summary>
public sealed class DirectorJobStore(
    [FromKeyedServices(DirectorJobStore.DataSourceKey)] NpgsqlDataSource dataSource,
    DirectorJobOptions options)
{
    /// <summary>Its own data source, without the command logging the app's has: the worker polls.</summary>
    public const string DataSourceKey = "director-jobs";

    /// <summary>Serialises claims across instances, so two workers never start the same lane at once.</summary>
    private const long ClaimLock = 0x5052_4F54_4A4F_4253;

    public const string GaveUp = "This kept stopping part-way through. Try again.";

    private static readonly string Running = nameof(DirectorJobStatus.Running);

    /// <summary>The oldest queued job whose lane has nothing running, or a running job whose worker stopped renewing it.</summary>
    /// <param name="onlyUser">Claims only this user's jobs, so tests sharing a database keep to their own.</param>
    public async Task<ClaimedJob?> ClaimAsync(CancellationToken ct, string? onlyUser = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", ClaimLock);
            await lockCommand.ExecuteNonQueryAsync(ct);
        }

        await using (var giveUp = new NpgsqlCommand(
                         """
                         UPDATE plot.director_jobs
                         SET status = 'Failed', error = @error, finished_at = now(), lease_until = NULL, date_last_modified = now()
                         WHERE status = 'Running' AND lease_until < now() AND attempts >= @max
                         """, connection, transaction))
        {
            giveUp.Parameters.AddWithValue("error", GaveUp);
            giveUp.Parameters.AddWithValue("max", options.MaxAttempts);
            await giveUp.ExecuteNonQueryAsync(ct);
        }

        await using var claim = new NpgsqlCommand(
            """
            WITH candidate AS (
                SELECT j.id FROM plot.director_jobs j
                WHERE (j.status = 'Queued' OR (j.status = 'Running' AND j.lease_until < now()))
                  AND (@user::text IS NULL OR j.user_id = @user)
                  AND NOT EXISTS (
                      SELECT 1 FROM plot.director_jobs r
                      WHERE r.lane = j.lane AND r.id <> j.id AND r.status = 'Running' AND r.lease_until >= now())
                  AND NOT EXISTS (
                      SELECT 1 FROM plot.director_jobs e
                      WHERE e.lane = j.lane AND e.status IN ('Queued', 'Running') AND e.id <> j.id
                        AND (e.date_created, e.id) < (j.date_created, j.id))
                ORDER BY j.date_created, j.id
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            )
            UPDATE plot.director_jobs j
            SET status = 'Running', attempts = j.attempts + 1, lease_until = now() + @lease,
                started_at = coalesce(j.started_at, now()), date_last_modified = now()
            FROM candidate
            WHERE j.id = candidate.id
            RETURNING j.id, j.user_id, j.label, j.steps, j.steps_done, j.follow_variations, j.results, j.attempts, j.trace_parent
            """, connection, transaction);
        claim.Parameters.AddWithValue("lease", options.Lease);
        claim.Parameters.Add(new NpgsqlParameter("user", NpgsqlDbType.Text) { Value = (object?)onlyUser ?? DBNull.Value });

        ClaimedJob? job = null;
        await using (var reader = await claim.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                job = new ClaimedJob(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    JobSteps.Parser.ParseFrom(reader.GetFieldValue<byte[]>(3)),
                    reader.GetInt32(4),
                    reader.GetBoolean(5),
                    reader.IsDBNull(6) ? new JobResults() : JobResults.Parser.ParseFrom(reader.GetFieldValue<byte[]>(6)),
                    reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8));
            }
        }

        await transaction.CommitAsync(ct);
        return job;
    }

    /// <summary>Renews the lease of a job still running.</summary>
    /// <returns>False when the job was cancelled or is gone, so its run should stop.</returns>
    public async Task<bool> HeartbeatAsync(Guid id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE plot.director_jobs SET lease_until = now() + @lease WHERE id = @id AND status = @running");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("lease", options.Lease);
        command.Parameters.AddWithValue("running", Running);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>Records how far the job got; the steps are written too, since following a variation can add one.</summary>
    public async Task RecordStepAsync(Guid id, int stepsDone, JobSteps steps, JobResults results, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE plot.director_jobs
            SET steps_done = @done, steps = @steps, step_count = @count, results = @results, date_last_modified = now()
            WHERE id = @id AND status = @running
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("done", stepsDone);
        command.Parameters.Add(new NpgsqlParameter("steps", NpgsqlDbType.Bytea) { Value = steps.ToByteArray() });
        command.Parameters.AddWithValue("count", steps.Steps.Count);
        command.Parameters.Add(new NpgsqlParameter("results", NpgsqlDbType.Bytea) { Value = results.ToByteArray() });
        command.Parameters.AddWithValue("running", Running);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task FinishAsync(Guid id, DirectorJobStatus status, string? error, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE plot.director_jobs
            SET status = @status, error = @error, finished_at = now(), lease_until = NULL, date_last_modified = now()
            WHERE id = @id AND status = @running
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("running", Running);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Hands a job back to the queue when its instance shuts down; the claim it used is not counted.</summary>
    public async Task ReleaseAsync(Guid id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE plot.director_jobs
            SET status = 'Queued', lease_until = NULL, attempts = greatest(attempts - 1, 0), date_last_modified = now()
            WHERE id = @id AND status = @running
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("running", Running);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM plot.director_jobs WHERE status IN ('Succeeded', 'Failed', 'Cancelled') AND finished_at < now() - @keep");
        command.Parameters.AddWithValue("keep", options.KeepFinishedFor);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
