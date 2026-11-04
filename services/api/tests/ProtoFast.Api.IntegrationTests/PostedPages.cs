using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Serves the pages tests put up, and a 404 for any other, keeping what was asked for.</summary>
public sealed class PostedPages : HttpMessageHandler, IHttpClientFactory
{
    private readonly ConcurrentDictionary<string, string> _pages = new();

    public ConcurrentQueue<string> Requested { get; } = new();

    public void Post(string url, string html) => _pages[url] = html;

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requested.Enqueue(url);
        return Task.FromResult(_pages.TryGetValue(url, out var html)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
