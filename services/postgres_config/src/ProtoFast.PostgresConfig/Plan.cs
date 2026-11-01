using Npgsql;

namespace ProtoFast.PostgresConfig;

/// <summary>DatabaseUser is a database and the user its connection string logs in as.</summary>
public sealed record DatabaseUser(string Database, string User, string Password)
{
    public static DatabaseUser From(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        return new DatabaseUser(
            b.Database ?? throw new InvalidOperationException("a connection string without a Database"),
            b.Username ?? throw new InvalidOperationException("a connection string without a Username"),
            b.Password ?? "");
    }

    public override string ToString() => $"{User} on {Database}"; // Never the password.
}

/// <summary>Step is one thing postgres-config makes so, stated as SQL the server quotes itself.</summary>
public abstract record Step(string Description)
{
    // A step may hold a password: it prints as its description alone.
    public sealed override string ToString() => Description;

    public abstract Task ApplyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken);

    // Run has the server quote names and values with format(), then runs the statement it
    // made, so no name or password is ever pasted into SQL here.
    protected static async Task RunFormattedAsync(NpgsqlDataSource dataSource, string format, string[] args, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        string statement;
        await using (var command = new NpgsqlCommand($"SELECT format('{format}', {string.Join(", ", args.Select((_, i) => $"${i + 1}"))})", connection))
        {
            foreach (var arg in args)
                command.Parameters.AddWithValue(arg);
            statement = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        await using var run = new NpgsqlCommand(statement, connection);
        await run.ExecuteNonQueryAsync(cancellationToken);
    }

    protected static async Task<bool> ExistsAsync(NpgsqlDataSource dataSource, string sql, string arg, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(arg);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}

/// <summary>EnsureUser makes a user that logs in with the password, or gives an existing one it.</summary>
public sealed record EnsureUser(string User, string Password) : Step($"user {User} logs in with its connection string's password")
{
    public override async Task ApplyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(dataSource, "SELECT 1 FROM pg_roles WHERE rolname = $1", User, cancellationToken))
            await RunFormattedAsync(dataSource, "CREATE ROLE %I LOGIN", [User], cancellationToken);
        await RunFormattedAsync(dataSource, "ALTER ROLE %I WITH LOGIN PASSWORD %L", [User, Password], cancellationToken);
    }
}

/// <summary>EnsureDatabase makes a database owned by its user, or gives an existing one to it.</summary>
public sealed record EnsureDatabase(string Database, string Owner) : Step($"database {Database} is owned by {Owner}")
{
    public override async Task ApplyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(dataSource, "SELECT 1 FROM pg_database WHERE datname = $1", Database, cancellationToken))
            await RunFormattedAsync(dataSource, "CREATE DATABASE %I OWNER %I", [Database, Owner], cancellationToken);
        else
            await RunFormattedAsync(dataSource, "ALTER DATABASE %I OWNER TO %I", [Database, Owner], cancellationToken);
    }
}

/// <summary>Plan is what postgres-config makes so, in order.</summary>
public static class Plan
{
    /// <summary>
    /// For plans each database: its own user first, unless it logs in as the superuser, as it
    /// does under `aspire run`, then the database, owned by that user.
    /// </summary>
    public static IReadOnlyList<Step> For(string superuser, IEnumerable<DatabaseUser> databases)
    {
        var steps = new List<Step>();
        foreach (var d in databases)
        {
            if (d.User != superuser && !steps.OfType<EnsureUser>().Any(u => u.User == d.User))
                steps.Add(new EnsureUser(d.User, d.Password));
            steps.Add(new EnsureDatabase(d.Database, d.User));
        }
        return steps;
    }
}
