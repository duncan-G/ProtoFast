using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ProtoFast.Api.Services.Imaging;
using ProtoFast.Api.Services.Speech;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Muse and ElevenLabs calls are GenAI client spans, with content only when capture is on.</summary>
public sealed class TracedMediaTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly ActivityListener _listener;

    public TracedMediaTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is ImagingTelemetry.SourceName or SpeechTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task A_picture_is_a_gen_ai_image_span()
    {
        var pictures = new TracedImageGenerator(new RecordingImageGenerator(), "meta", "muse-image-1.0", "api.meta.ai", captureContent: false);

        await pictures.GenerateAsync("A diner at night.", [], Ct);

        var span = Assert.Single(_spans);
        Assert.Equal(("generate_content muse-image-1.0", ActivityKind.Client), (span.DisplayName, span.Kind));
        Assert.Equal("generate_content", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("meta", span.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("muse-image-1.0", span.GetTagItem("gen_ai.request.model"));
        Assert.Equal("image", span.GetTagItem("gen_ai.output.type"));
        Assert.Equal("api.meta.ai", span.GetTagItem("server.address"));
        Assert.Equal("resp_1", span.GetTagItem("gen_ai.response.id"));
        Assert.Equal("muse-image-1.0-20261001", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal((1200L, 1000L, 1L), (
            (long?)span.GetTagItem("gen_ai.usage.input_tokens"),
            (long?)span.GetTagItem("gen_ai.usage.cache_read.input_tokens"),
            (long?)span.GetTagItem("gen_ai.usage.output_tokens")));
        Assert.Equal(["completed"], (string[])span.GetTagItem("gen_ai.response.finish_reasons")!);
        Assert.Null(span.GetTagItem("gen_ai.input.messages"));
        Assert.Null(span.GetTagItem("gen_ai.output.messages"));
    }

    [Fact]
    public async Task A_captured_picture_carries_the_references_the_prompt_the_notes_and_the_image()
    {
        var pictures = new TracedImageGenerator(new RecordingImageGenerator(), "meta", "muse-image-1.0", "api.meta.ai", captureContent: true);

        await pictures.GenerateAsync("A diner at night.", [new ImageInput([0x01, 0x02], "image/webp", "The diner from the door.")], Ct);

        var span = Assert.Single(_spans);
        var input = Json(span, "gen_ai.input.messages")[0].GetProperty("parts");
        Assert.Equal("The diner from the door.", input[0].GetProperty("content").GetString());
        Assert.Equal(("blob", "image"), (input[1].GetProperty("type").GetString(), input[1].GetProperty("modality").GetString()));
        Assert.Equal(new byte[] { 0x01, 0x02 }, Convert.FromBase64String(input[1].GetProperty("content").GetString()!));
        Assert.Equal("A diner at night.", input[2].GetProperty("content").GetString());
        var parts = Json(span, "gen_ai.output.messages")[0].GetProperty("parts");
        Assert.Equal(("reasoning", "Kept the counter in view."), (parts[0].GetProperty("type").GetString(), parts[0].GetProperty("content").GetString()));
        Assert.Equal(("blob", "image", "image/webp"), (
            parts[1].GetProperty("type").GetString(), parts[1].GetProperty("modality").GetString(), parts[1].GetProperty("mime_type").GetString()));
        Assert.Equal(RecordingImageGenerator.Picture, Convert.FromBase64String(parts[1].GetProperty("content").GetString()!));
    }

    [Fact]
    public async Task A_spoken_line_is_a_gen_ai_speech_span_with_the_words_and_audio_when_captured()
    {
        var voices = new TracedSpeechSynthesizer(new RecordingSpeechSynthesizer(), "elevenlabs", "eleven_v4", "api.elevenlabs.io", captureContent: true);

        await voices.SpeakAsync("We're closed.", new SpeechVoice("v1", "Adam"), Ct);

        var span = Assert.Single(_spans);
        Assert.Equal("generate_content eleven_v4", span.DisplayName);
        Assert.Equal("elevenlabs", span.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("eleven_v4", span.GetTagItem("gen_ai.request.model"));
        Assert.Equal("speech", span.GetTagItem("gen_ai.output.type"));
        Assert.Equal("req_1", span.GetTagItem("gen_ai.response.id"));
        Assert.Equal(("v1", "Adam", 13), (
            span.GetTagItem("protofast.speech.voice"), span.GetTagItem("protofast.speech.voice_name"), span.GetTagItem("protofast.speech.characters")));
        Assert.Equal("We're closed.", Json(span, "gen_ai.input.messages")[0].GetProperty("parts")[0].GetProperty("content").GetString());
        var audio = Json(span, "gen_ai.output.messages")[0].GetProperty("parts")[0];
        Assert.Equal(("blob", "audio", "audio/mpeg"), (
            audio.GetProperty("type").GetString(), audio.GetProperty("modality").GetString(), audio.GetProperty("mime_type").GetString()));
    }

    private static JsonElement Json(Activity span, string tag) =>
        JsonDocument.Parse((string)span.GetTagItem(tag)!).RootElement;
}
