namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>
/// Reads a posting's text off its page to keep beside the application. The model doesn't copy it out:
/// Gemini stops a reply that recites a page.
/// </summary>
public sealed class PostingPages(IHttpClientFactory httpClients, ILogger<PostingPages> logger)
{
    public const string HttpClientName = "posting-pages";

    /// <summary>The posting on the first of the pages that shows one; empty when none does.</summary>
    public async Task<string> ReadAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        foreach (var url in urls)
        {
            var posting = await ReadAsync(url, cancellationToken);
            if (posting.Length > 0)
            {
                return posting;
            }
        }

        return "";
    }

    private async Task<string> ReadAsync(string url, CancellationToken cancellationToken)
    {
        var host = new Uri(url).Host;
        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).GetAsync(url, cancellationToken);
            var type = response.Content.Headers.ContentType?.MediaType;
            if (response.IsSuccessStatusCode && type is "text/html" or "application/xhtml+xml")
            {
                var posting = PostingText.Read(await response.Content.ReadAsStringAsync(cancellationToken));
                if (posting.Length > 0)
                {
                    return posting;
                }
            }

            logger.LogInformation(
                "No posting on {Host}: it answered {StatusCode} with {MediaType}", host, (int)response.StatusCode, type);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(e, "No posting on {Host}: it couldn't be fetched", host);
        }

        return "";
    }
}
