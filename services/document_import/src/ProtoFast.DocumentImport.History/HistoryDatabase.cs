using Npgsql;

namespace ProtoFast.DocumentImport.History;

/// <summary>The development-only database of historical documents, and the story runs written from it.</summary>
public sealed class HistoryDatabase(NpgsqlDataSource dataSource)
{
    public const string ConnectionName = "history";

    public NpgsqlDataSource DataSource { get; } = dataSource;
}
