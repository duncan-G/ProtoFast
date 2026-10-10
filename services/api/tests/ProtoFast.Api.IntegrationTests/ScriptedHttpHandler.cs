using System.Net;
using System.Text;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Answers each call with the next scripted reply.</summary>
public sealed class ScriptedHttpHandler(params (HttpStatusCode Status, string Body)[] replies) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var (status, body) = replies[Calls++];
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
