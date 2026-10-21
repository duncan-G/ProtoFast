using System.Net;
using Microsoft.Extensions.Options;
using ProtoFast.DocumentImport.History.Gutenberg;

namespace ProtoFast.DocumentImport.HistoryLoader;

/// <summary>Fetches the catalog and ebook texts through the cache directory, retrying transient failures.</summary>
public sealed class GutenbergDownloader(HttpClient http, IOptions<HistoryLoaderOptions> options)
{
    private const int Attempts = 3;

    private string CacheDirectory => options.Value.CacheDirectory;

    public async Task<string> CatalogAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDirectory);
        var path = Path.Combine(CacheDirectory, "pg_catalog.csv");
        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(path, await FetchAsync(GutenbergCatalog.CatalogUrl, ct), ct);
        }

        return path;
    }

    public async Task<string> TextAsync(int bookId, CancellationToken ct)
    {
        var path = Path.Combine(CacheDirectory, $"pg{bookId}.txt");
        if (File.Exists(path))
        {
            return await File.ReadAllTextAsync(path, ct);
        }

        var text = await FetchAsync(GutenbergText.TextUrl(bookId), ct);
        await File.WriteAllTextAsync(path, text, ct);
        await Task.Delay(options.Value.DownloadDelay, ct);
        return text;
    }

    private async Task<string> FetchAsync(string url, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await http.GetStringAsync(url, ct);
            }
            catch (HttpRequestException e) when (attempt < Attempts && e.StatusCode is not HttpStatusCode.NotFound)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }
    }
}
