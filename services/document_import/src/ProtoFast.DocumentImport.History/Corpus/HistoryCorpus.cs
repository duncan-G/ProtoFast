using Npgsql;
using NpgsqlTypes;

namespace ProtoFast.DocumentImport.History.Corpus;

/// <summary>
/// The historical documents: books cut into passages, searched with Postgres full text. A year
/// filter keeps books whose subjects overlap the range, and books whose subjects name no year.
/// </summary>
public sealed class HistoryCorpus(HistoryDatabase database)
{
    private const string PassageColumns = "p.id, p.book_id, p.seq, b.title, b.authors, b.first_year, b.last_year";

    private const string YearFilter = """
        (@from_year IS NULL OR b.last_year IS NULL OR b.last_year >= @from_year)
        AND (@to_year IS NULL OR b.first_year IS NULL OR b.first_year <= @to_year)
        """;

    // Front and back matter (contents, prefaces, indexes) sits at the ends of a book.
    private const double SkipFraction = 0.1;

    private NpgsqlDataSource DataSource => database.DataSource;

    public async Task<CorpusStats> StatsAsync(CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT (SELECT count(*) FROM history.books), (SELECT coalesce(sum(passages), 0) FROM history.books)");
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new CorpusStats(reader.GetInt64(0), reader.GetInt64(1));
    }

    /// <param name="query">Web search syntax: words, "quoted phrases", OR, and -excluded words.</param>
    public async Task<IReadOnlyList<CorpusHit>> SearchAsync(string query, int? fromYear, int? toYear, int limit, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"""
            WITH q AS (SELECT websearch_to_tsquery('english', @query) AS query),
            hits AS (
                SELECT p.id, ts_rank_cd(p.tsv, q.query) AS rank
                FROM history.passages p
                JOIN history.books b ON b.id = p.book_id
                CROSS JOIN q
                WHERE p.tsv @@ q.query AND {YearFilter}
                ORDER BY rank DESC
                LIMIT @limit
            )
            SELECT {PassageColumns},
                   ts_headline('english', p.text, q.query, 'MaxWords=45, MinWords=20, MaxFragments=2, FragmentDelimiter=" … "'),
                   h.rank
            FROM hits h
            JOIN history.passages p ON p.id = h.id
            JOIN history.books b ON b.id = p.book_id
            CROSS JOIN q
            ORDER BY h.rank DESC
            """);
        command.Parameters.AddWithValue("query", query);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 50));
        AddYears(command, fromYear, toYear);

        var hits = new List<CorpusHit>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            hits.Add(new CorpusHit(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
                NullableInt(reader, 5), NullableInt(reader, 6), reader.GetString(7), reader.GetFloat(8)));
        }

        return hits;
    }

    /// <summary>The passage and its neighbours in the same book, in book order; empty when there is no such passage.</summary>
    public async Task<IReadOnlyList<CorpusPassage>> ReadAsync(long passageId, int before, int after, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"""
            SELECT {PassageColumns}, p.text
            FROM history.passages target
            JOIN history.passages p ON p.book_id = target.book_id AND p.seq BETWEEN target.seq - @before AND target.seq + @after
            JOIN history.books b ON b.id = p.book_id
            WHERE target.id = @id
            ORDER BY p.seq
            """);
        command.Parameters.AddWithValue("id", passageId);
        command.Parameters.AddWithValue("before", Math.Clamp(before, 0, 3));
        command.Parameters.AddWithValue("after", Math.Clamp(after, 0, 3));
        return await ReadPassagesAsync(command, ct);
    }

    public async Task<IReadOnlyList<CorpusPassage>> GetAsync(IReadOnlyCollection<long> passageIds, CancellationToken ct)
    {
        if (passageIds.Count == 0)
        {
            return [];
        }

        await using var command = DataSource.CreateCommand($"""
            SELECT {PassageColumns}, p.text
            FROM history.passages p
            JOIN history.books b ON b.id = p.book_id
            WHERE p.id = ANY(@ids)
            ORDER BY p.book_id, p.seq
            """);
        command.Parameters.AddWithValue("ids", passageIds.ToArray());
        return await ReadPassagesAsync(command, ct);
    }

    /// <summary>One passage from each of up to <paramref name="count"/> random books, taken away from the book's ends.</summary>
    public async Task<IReadOnlyList<CorpusPassage>> RandomAsync(int count, int? fromYear, int? toYear, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"""
            SELECT {PassageColumns}, p.text
            FROM (
                SELECT b.*, floor(b.passages * (@skip + random() * (1 - 2 * @skip)))::int AS target
                FROM history.books b
                WHERE b.passages >= 8 AND {YearFilter}
                ORDER BY random()
                LIMIT @count
            ) b
            JOIN history.passages p ON p.book_id = b.id AND p.seq = b.target
            """);
        command.Parameters.AddWithValue("skip", SkipFraction);
        command.Parameters.AddWithValue("count", Math.Clamp(count, 1, 20));
        AddYears(command, fromYear, toYear);
        return await ReadPassagesAsync(command, ct);
    }

    public async Task<HashSet<int>> LoadedBookIdsAsync(CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand("SELECT id FROM history.books");
        var ids = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    /// <summary>Adds the book and its passages in one transaction; false when the book is already loaded.</summary>
    public async Task<bool> AddBookAsync(CorpusBook book, IReadOnlyList<string> passages, CancellationToken ct)
    {
        await using var connection = await DataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO history.books (id, title, authors, issued, subjects, locc, bookshelves, first_year, last_year, passages)
            VALUES (@id, @title, @authors, @issued, @subjects, @locc, @bookshelves, @first_year, @last_year, @passages)
            ON CONFLICT (id) DO NOTHING
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", book.Id);
            insert.Parameters.AddWithValue("title", book.Title);
            insert.Parameters.AddWithValue("authors", book.Authors);
            insert.Parameters.AddWithValue("issued", book.Issued);
            insert.Parameters.AddWithValue("subjects", book.Subjects);
            insert.Parameters.AddWithValue("locc", book.Locc);
            insert.Parameters.AddWithValue("bookshelves", book.Bookshelves);
            insert.Parameters.Add(new NpgsqlParameter<int?>("first_year", NpgsqlDbType.Integer) { TypedValue = book.FirstYear });
            insert.Parameters.Add(new NpgsqlParameter<int?>("last_year", NpgsqlDbType.Integer) { TypedValue = book.LastYear });
            insert.Parameters.AddWithValue("passages", passages.Count);
            if (await insert.ExecuteNonQueryAsync(ct) == 0)
            {
                return false;
            }
        }

        await using (var copy = await connection.BeginBinaryImportAsync(
            "COPY history.passages (book_id, seq, text) FROM STDIN (FORMAT BINARY)", ct))
        {
            for (var seq = 0; seq < passages.Count; seq++)
            {
                await copy.StartRowAsync(ct);
                await copy.WriteAsync(book.Id, NpgsqlDbType.Integer, ct);
                await copy.WriteAsync(seq, NpgsqlDbType.Integer, ct);
                await copy.WriteAsync(passages[seq], NpgsqlDbType.Text, ct);
            }

            await copy.CompleteAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    private static void AddYears(NpgsqlCommand command, int? fromYear, int? toYear)
    {
        command.Parameters.Add(new NpgsqlParameter<int?>("from_year", NpgsqlDbType.Integer) { TypedValue = fromYear });
        command.Parameters.Add(new NpgsqlParameter<int?>("to_year", NpgsqlDbType.Integer) { TypedValue = toYear });
    }

    private static async Task<IReadOnlyList<CorpusPassage>> ReadPassagesAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var passages = new List<CorpusPassage>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            passages.Add(new CorpusPassage(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
                NullableInt(reader, 5), NullableInt(reader, 6), reader.GetString(7)));
        }

        return passages;
    }

    private static int? NullableInt(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
}
