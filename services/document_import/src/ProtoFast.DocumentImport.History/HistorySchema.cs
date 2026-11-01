using Npgsql;

namespace ProtoFast.DocumentImport.History;

/// <summary>
/// The corpus and run tables, created in place rather than migrated: the database holds a
/// downloaded corpus and dev runs only, so a change here is an edit to this script.
/// </summary>
public static class HistorySchema
{
    public const string Name = "history";

    // Serialises concurrent first starts of the api, worker and loader.
    private const long LockKey = 4_847_331;

    private const string Script = """
        CREATE SCHEMA IF NOT EXISTS history;

        CREATE TABLE IF NOT EXISTS history.books (
            id integer PRIMARY KEY,
            title text NOT NULL,
            authors text NOT NULL DEFAULT '',
            issued text NOT NULL DEFAULT '',
            subjects text NOT NULL DEFAULT '',
            locc text NOT NULL DEFAULT '',
            bookshelves text NOT NULL DEFAULT '',
            first_year integer,
            last_year integer,
            passages integer NOT NULL,
            loaded_at timestamptz NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS history.passages (
            id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            book_id integer NOT NULL REFERENCES history.books(id) ON DELETE CASCADE,
            seq integer NOT NULL,
            text text NOT NULL,
            tsv tsvector GENERATED ALWAYS AS (to_tsvector('english', text)) STORED,
            UNIQUE (book_id, seq)
        );

        CREATE INDEX IF NOT EXISTS ix_passages_tsv ON history.passages USING gin (tsv);

        CREATE TABLE IF NOT EXISTS history.story_runs (
            id uuid PRIMARY KEY,
            writer_id text NOT NULL,
            operator_id text NOT NULL,
            request text,
            scenes integer NOT NULL,
            status text NOT NULL,
            message text NOT NULL DEFAULT '',
            title text,
            brief jsonb,
            story_id uuid,
            failure text,
            cost numeric(18,6) NOT NULL DEFAULT 0,
            attempts integer NOT NULL DEFAULT 0,
            heartbeat_at timestamptz,
            created_at timestamptz NOT NULL DEFAULT now(),
            started_at timestamptz,
            finished_at timestamptz
        );

        CREATE INDEX IF NOT EXISTS ix_story_runs_created_at ON history.story_runs (created_at DESC);

        CREATE TABLE IF NOT EXISTS history.story_run_events (
            run_id uuid NOT NULL REFERENCES history.story_runs(id) ON DELETE CASCADE,
            seq integer NOT NULL,
            at timestamptz NOT NULL DEFAULT now(),
            kind text NOT NULL,
            summary text NOT NULL,
            detail text,
            cost numeric(18,6) NOT NULL DEFAULT 0,
            PRIMARY KEY (run_id, seq)
        );
        """;

    public static async Task EnsureAsync(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var lockCommand = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({LockKey})", connection, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync(ct);
        }

        await using (var command = new NpgsqlCommand(Script, connection, transaction))
        {
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}
