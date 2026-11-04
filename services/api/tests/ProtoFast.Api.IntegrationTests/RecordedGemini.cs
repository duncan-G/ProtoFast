using System.Net;
using System.Text;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>
/// Answers each request with the next reply's server-sent events, keeping what was asked; while
/// <see cref="Outages"/> holds a status, a request is answered with that instead.
/// </summary>
public sealed class RecordedGemini(params string[][] replies) : HttpMessageHandler, IHttpClientFactory
{
    private int _replied;

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    public Queue<HttpStatusCode> Outages { get; } = new();

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
        if (Outages.TryDequeue(out var status))
        {
            return new HttpResponseMessage(status) { Content = new StringContent("""{"error":{"status":"UNAVAILABLE"}}""") };
        }

        var events = replies[_replied++];
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Concat(events.Select(e => $"data: {e}\r\n\r\n")), Encoding.UTF8, "text/event-stream"),
        };
    }
}
