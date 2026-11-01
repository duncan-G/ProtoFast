using Npgsql;
using NpgsqlTypes;

namespace ProtoFast.DocumentImport.History.Runs;

/// <summary>
/// The story runs and what each did, which is also the queue the worker takes runs from. A run's
/// status only moves forward while it is active; Finished, Failed and Cancelled are final.
/// </summary>
public sealed class StoryRunLedger(HistoryDatabase database)
{
    // Detail is kept for the console, not for replay; a passage read is a few thousand characters.
    private const int MaxDetailChars = 40_000;

    private const string Columns = """
        id, writer_id, operator_id, request, scenes, status, message, title, brief::text, story_id, failure, cost,
        attempts, created_at, started_at, finished_at
        """;

    private const string Active = "status IN ('Queued', 'Researching', 'Writing', 'Saving')";

    private NpgsqlDataSource DataSource => database.DataSource;

    public async Task<Guid> CreateAsync(string writerId, string operatorId, string? request, int scenes, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await using var command = DataSource.CreateCommand("""
            INSERT INTO history.story_runs (id, writer_id, operator_id, request, scenes, status, message)
            VALUES (@id, @writer_id, @operator_id, @request, @scenes, 'Queued', 'Waiting for a worker')
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("writer_id", writerId);
        command.Parameters.AddWithValue("operator_id", operatorId);
        command.Parameters.Add(new NpgsqlParameter<string?>("request", NpgsqlDbType.Text) { TypedValue = request });
        command.Parameters.AddWithValue("scenes", scenes);
        await command.ExecuteNonQueryAsync(ct);
        return id;
    }

    /// <summary>
    /// Takes the oldest queued run, or an active one whose worker stopped beating, and marks it
    /// researching. A stale run that has used its attempts is failed instead. One statement, since
    /// the worker polls with it.
    /// </summary>
    public async Task<StoryRun?> ClaimAsync(TimeSpan staleAfter, int maxAttempts, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"""
            WITH expired AS (
                UPDATE history.story_runs
                SET status = 'Failed', failure = coalesce(failure, 'The worker stopped responding.'),
                    message = 'Failed', finished_at = now()
                WHERE {Active} AND status <> 'Queued' AND heartbeat_at < now() - @stale AND attempts >= @max_attempts
            )
            UPDATE history.story_runs
            SET status = 'Researching', message = 'Starting', attempts = attempts + 1, heartbeat_at = now(),
                started_at = coalesce(started_at, now()), failure = NULL
            WHERE id = (
                SELECT id FROM history.story_runs
                WHERE status = 'Queued' OR ({Active} AND heartbeat_at < now() - @stale AND attempts < @max_attempts)
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING {Columns}
            """);
        command.Parameters.AddWithValue("stale", staleAfter);
        command.Parameters.AddWithValue("max_attempts", maxAttempts);
        return (await ReadRunsAsync(command, ct)).FirstOrDefault();
    }

    /// <summary>Marks the run alive; returns its status, so a cancelled run can stop.</summary>
    public async Task<StoryRunStatus?> HeartbeatAsync(Guid id, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"""
            UPDATE history.story_runs SET heartbeat_at = CASE WHEN {Active} THEN now() ELSE heartbeat_at END
            WHERE id = @id
            RETURNING status
            """);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(ct) is string status ? Enum.Parse<StoryRunStatus>(status) : null;
    }

    public async Task ProgressAsync(Guid id, StoryRunStatus status, string message, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"""
            UPDATE history.story_runs SET status = @status, message = @message, heartbeat_at = now()
            WHERE id = @id AND {Active}
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("message", message);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Records what the run did, adding its cost to the run's total.</summary>
    public async Task AddEventAsync(Guid id, string kind, string summary, string? detail, decimal cost, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand("""
            WITH run AS (UPDATE history.story_runs SET cost = cost + @cost WHERE id = @id RETURNING id)
            INSERT INTO history.story_run_events (run_id, seq, kind, summary, detail, cost)
            SELECT run.id, coalesce((SELECT max(seq) FROM history.story_run_events WHERE run_id = @id), 0) + 1,
                   @kind, @summary, @detail, @cost
            FROM run
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("summary", summary);
        command.Parameters.Add(new NpgsqlParameter<string?>("detail", NpgsqlDbType.Text)
        {
            TypedValue = detail is { Length: > MaxDetailChars } ? detail[..MaxDetailChars] + "\n…" : detail,
        });
        command.Parameters.AddWithValue("cost", cost);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SetBriefAsync(Guid id, string title, string briefJson, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand(
            "UPDATE history.story_runs SET title = @title, brief = @brief::jsonb WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("brief", briefJson);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task FinishAsync(Guid id, Guid storyId, string title, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"""
            UPDATE history.story_runs
            SET status = 'Finished', message = 'Written', story_id = @story_id, title = @title, finished_at = now()
            WHERE id = @id AND {Active}
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("story_id", storyId);
        command.Parameters.AddWithValue("title", title);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Fails the run, or queues it again while it has attempts left.</summary>
    public async Task FailAsync(Guid id, string failure, bool final, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"""
            UPDATE history.story_runs
            SET status = CASE WHEN @final THEN 'Failed' ELSE 'Queued' END,
                message = CASE WHEN @final THEN 'Failed' ELSE 'Retrying' END,
                failure = @failure,
                finished_at = CASE WHEN @final THEN now() ELSE NULL END
            WHERE id = @id AND {Active}
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("failure", failure);
        command.Parameters.AddWithValue("final", final);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>False when the run is not active.</summary>
    public async Task<bool> CancelAsync(Guid id, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"""
            UPDATE history.story_runs SET status = 'Cancelled', message = 'Cancelled', finished_at = now()
            WHERE id = @id AND {Active}
            """);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<StoryRunPage> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"""
            SELECT {Columns} FROM history.story_runs ORDER BY created_at DESC OFFSET @offset LIMIT @limit
            """);
        command.Parameters.AddWithValue("offset", page * pageSize);
        command.Parameters.AddWithValue("limit", pageSize);
        var runs = await ReadRunsAsync(command, ct);

        await using var count = DataSource.CreateCommand("SELECT count(*) FROM history.story_runs");
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        return new StoryRunPage(runs, total);
    }

    public async Task<StoryRun?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"SELECT {Columns} FROM history.story_runs WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        return (await ReadRunsAsync(command, ct)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<StoryRunEvent>> EventsAsync(Guid id, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand("""
            SELECT seq, at, kind, summary, detail, cost FROM history.story_run_events WHERE run_id = @id ORDER BY seq
            """);
        command.Parameters.AddWithValue("id", id);
        var events = new List<StoryRunEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(new StoryRunEvent(
                reader.GetInt32(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDecimal(5)));
        }

        return events;
    }

    private static async Task<IReadOnlyList<StoryRun>> ReadRunsAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var runs = new List<StoryRun>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            runs.Add(new StoryRun(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4),
                Enum.Parse<StoryRunStatus>(reader.GetString(5)),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetDecimal(11),
                reader.GetInt32(12),
                reader.GetFieldValue<DateTimeOffset>(13),
                reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
                reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15)));
        }

        return runs;
    }
}
