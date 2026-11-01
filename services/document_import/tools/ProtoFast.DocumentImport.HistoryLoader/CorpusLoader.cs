using Microsoft.Extensions.Options;
using ProtoFast.DocumentImport.History.Corpus;
using ProtoFast.DocumentImport.History.Gutenberg;

namespace ProtoFast.DocumentImport.HistoryLoader;

/// <summary>Loads the catalog's American history books that the corpus lacks, one transaction per book.</summary>
public sealed class CorpusLoader(
    HistoryCorpus corpus, GutenbergDownloader downloader, IOptions<HistoryLoaderOptions> options, ILogger<CorpusLoader> logger)
{
    private const int ReportEvery = 25;

    public async Task RunAsync(CancellationToken ct)
    {
        var catalogPath = await downloader.CatalogAsync(ct);
        List<CorpusBook> books;
        using (var catalog = File.OpenText(catalogPath))
        {
            books = GutenbergCatalog.AmericanHistory(catalog).ToList();
        }

        var loaded = await corpus.LoadedBookIdsAsync(ct);
        var todo = books.Where(b => !loaded.Contains(b.Id)).ToList();
        if (options.Value.Limit > 0)
        {
            todo = todo.Take(options.Value.Limit).ToList();
        }

        logger.LogInformation(
            "{Books} American history books in the catalog; {Loaded} already loaded; loading {Todo}",
            books.Count, loaded.Count, todo.Count);

        var added = 0;
        var failed = 0;
        await Parallel.ForEachAsync(
            todo,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.Workers), CancellationToken = ct },
            async (book, token) =>
            {
                try
                {
                    var passages = GutenbergText.Passages(GutenbergText.StripBoilerplate(await downloader.TextAsync(book.Id, token)));
                    if (passages.Count == 0 || !await corpus.AddBookAsync(book, passages, token))
                    {
                        return;
                    }

                    if (Interlocked.Increment(ref added) % ReportEvery == 0)
                    {
                        logger.LogInformation("Loaded {Added} of {Todo}", added, todo.Count);
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Interlocked.Increment(ref failed);
                    logger.LogWarning("Book {BookId} ({Title}) failed: {Error}", book.Id, book.Title, e.Message);
                }
            });

        var stats = await corpus.StatsAsync(ct);
        logger.LogInformation(
            "Done: {Added} books added, {Failed} failed; the corpus holds {Books} books in {Passages} passages",
            added, failed, stats.Books, stats.Passages);
    }
}
