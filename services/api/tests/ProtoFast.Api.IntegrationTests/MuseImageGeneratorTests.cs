using System.Net;
using ProtoFast.Api.Services.Imaging;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public sealed class MuseImageGeneratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Picture = """
        {"id":"resp_1","model":"muse-image-1.0","status":"completed",
         "output":[{"type":"image_generation_call","result":"AQID"}]}
        """;

    private const string TimedOut = """{"error":{"code":null,"message":"request timed out","param":null,"type":"server_error"}}""";

    private static ImageRequest Request() => new("1536x1024", "", [], "Mara at the radio.");

    private static MuseImageGenerator Generator(ScriptedHttpHandler handler) => new(
        new MuseImageOptions { ApiKey = "key", RetryDelay = TimeSpan.Zero },
        new HttpClient(handler));

    [Fact]
    public async Task A_timed_out_call_is_retried_until_it_answers()
    {
        var handler = new ScriptedHttpHandler(
            (HttpStatusCode.GatewayTimeout, TimedOut),
            (HttpStatusCode.GatewayTimeout, TimedOut),
            (HttpStatusCode.OK, Picture));

        var image = await Generator(handler).GenerateAsync(Request(), Ct);

        Assert.Equal([1, 2, 3], image.Bytes);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task It_gives_up_after_three_retries()
    {
        var handler = new ScriptedHttpHandler(Enumerable.Repeat((HttpStatusCode.GatewayTimeout, TimedOut), 5).ToArray());

        var e = await Assert.ThrowsAsync<ImageException>(() => Generator(handler).GenerateAsync(Request(), Ct));

        Assert.StartsWith("Muse refused the picture (504)", e.Message);
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task A_bad_request_is_not_retried()
    {
        var handler = new ScriptedHttpHandler((HttpStatusCode.BadRequest, """{"error":"nope"}"""), (HttpStatusCode.OK, Picture));

        await Assert.ThrowsAsync<ImageException>(() => Generator(handler).GenerateAsync(Request(), Ct));

        Assert.Equal(1, handler.Calls);
    }
}
