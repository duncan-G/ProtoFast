using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ProtoFast.Api.Services.Imaging;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public sealed class TracedImageGeneratorTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly ActivityListener _listener;

    public TracedImageGeneratorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ImagingTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private static ImageRequest Request() => new(
        "1536x1024",
        "References follow.",
        [new ImageInput([1, 2, 3], "image/webp", "Image 1: Mara.", "stories/s/images/abc.webp")],
        "The picture to make: Mara at the radio.");

    [Fact]
    public async Task A_call_is_a_gen_ai_generate_content_span()
    {
        var generator = new TracedImageGenerator(new ScriptedImageGenerator(), "api.meta.ai", captureContent: false);

        await generator.GenerateAsync(Request(), Ct);

        var span = Span();
        Assert.Equal("generate_content scripted-1", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal("generate_content", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("scripted", span.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("scripted-1", span.GetTagItem("gen_ai.request.model"));
        Assert.Equal("image", span.GetTagItem("gen_ai.output.type"));
        Assert.Equal("api.meta.ai", span.GetTagItem("server.address"));
        Assert.Equal("scripted-1-0901", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal("resp_1", span.GetTagItem("gen_ai.response.id"));
        Assert.Equal(1200L, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(900L, span.GetTagItem("gen_ai.usage.cache_read.input_tokens"));
        Assert.Equal(80L, span.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Equal(["completed"], (string[])span.GetTagItem("gen_ai.response.finish_reasons")!);
        Assert.Equal(1, span.GetTagItem("protofast.image.references"));
        Assert.Null(span.GetTagItem("gen_ai.input.messages"));
        Assert.Null(span.GetTagItem("gen_ai.output.messages"));
    }

    [Fact]
    public async Task Captured_content_carries_the_references_and_the_picture_for_the_dashboard()
    {
        var generator = new TracedImageGenerator(new ScriptedImageGenerator(), "api.meta.ai", captureContent: true);

        await generator.GenerateAsync(Request(), Ct);

        var span = Span();
        var input = Json(span, "gen_ai.input.messages")[0].GetProperty("parts");
        Assert.Equal(
            ["References follow.", "Image 1: Mara.", "The picture to make: Mara at the radio."],
            input.EnumerateArray().Where(p => p.GetProperty("type").GetString() == "text")
                .Select(p => p.GetProperty("content").GetString()));
        var reference = input.EnumerateArray().Single(p => p.GetProperty("type").GetString() == "blob");
        Assert.Equal("image", reference.GetProperty("modality").GetString());
        Assert.Equal("image/webp", reference.GetProperty("mime_type").GetString());
        Assert.Equal([1, 2, 3], Convert.FromBase64String(reference.GetProperty("content").GetString()!));

        var output = Json(span, "gen_ai.output.messages")[0];
        Assert.Equal("completed", output.GetProperty("finish_reason").GetString());
        var parts = output.GetProperty("parts").EnumerateArray().ToList();
        Assert.Equal("Drew it from 1 references.", parts[0].GetProperty("content").GetString());
        Assert.Equal("blob", parts[1].GetProperty("type").GetString());
        Assert.Equal("image", parts[1].GetProperty("modality").GetString());
        Assert.Equal("image/webp", parts[1].GetProperty("mime_type").GetString());
        Assert.Equal(32, Convert.FromBase64String(parts[1].GetProperty("content").GetString()!).Length);
    }

    [Fact]
    public async Task References_past_the_budget_are_named_by_where_they_are_stored()
    {
        var generator = new TracedImageGenerator(new ScriptedImageGenerator(), "api.meta.ai", captureContent: true);
        var big = new byte[700 * 1024];

        await generator.GenerateAsync(Request() with
        {
            References =
            [
                new ImageInput(big, "image/webp", "Image 1: Mara.", "stories/s/images/one.webp"),
                new ImageInput(big, "image/webp", "Image 2: the radio.", "stories/s/images/two.webp"),
            ],
        }, Ct);

        var images = Json(Span(), "gen_ai.input.messages")[0].GetProperty("parts").EnumerateArray()
            .Where(p => p.GetProperty("type").GetString() != "text").ToList();
        Assert.Equal(["blob", "uri"], images.Select(p => p.GetProperty("type").GetString()));
        Assert.Equal("stories/s/images/two.webp", images[1].GetProperty("uri").GetString());
    }

    [Fact]
    public async Task A_failed_call_marks_the_span_as_an_error()
    {
        var generator = new TracedImageGenerator(new FailingImageGenerator(), "api.meta.ai", captureContent: false);

        await Assert.ThrowsAsync<ImageException>(() => generator.GenerateAsync(Request(), Ct));

        var span = Span();
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(typeof(ImageException).FullName, span.GetTagItem("error.type"));
    }

    private Activity Span() => Assert.Single(_spans);

    private static JsonElement[] Json(Activity span, string tag) =>
        JsonSerializer.Deserialize<JsonElement[]>((string)span.GetTagItem(tag)!)!;

    private sealed class FailingImageGenerator : IImageGenerator
    {
        public string Provider => "meta";

        public string Model => "muse-image-1.0";

        public Task<GeneratedImage> GenerateAsync(ImageRequest request, CancellationToken ct) =>
            throw new ImageException("Muse refused the picture (400): nope");
    }
}
