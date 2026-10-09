using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ProtoFast.Api.Services.Speech;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public sealed class TracedSpeechSynthesizerTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly ActivityListener _listener;

    public TracedSpeechSynthesizerTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SpeechTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private static SpeechRequest Request() => new("Is anyone there?", null, "v1");

    [Fact]
    public async Task Speaking_a_line_is_a_gen_ai_generate_content_span()
    {
        var synthesizer = new TracedSpeechSynthesizer(new ScriptedSpeechSynthesizer(), "api.elevenlabs.io", captureContent: false);

        await synthesizer.SpeakAsync(Request(), Ct);

        var span = Span();
        Assert.Equal("generate_content scripted-1", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal("generate_content", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("scripted", span.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("scripted-1", span.GetTagItem("gen_ai.request.model"));
        Assert.Equal("speech", span.GetTagItem("gen_ai.output.type"));
        Assert.Equal("api.elevenlabs.io", span.GetTagItem("server.address"));
        Assert.Equal("v1", span.GetTagItem("protofast.speech.voice"));
        Assert.Null(span.GetTagItem("gen_ai.input.messages"));
        Assert.Null(span.GetTagItem("gen_ai.output.messages"));
    }

    [Fact]
    public async Task Captured_content_carries_the_words_and_the_audio_for_the_dashboard()
    {
        var synthesizer = new TracedSpeechSynthesizer(new ScriptedSpeechSynthesizer(), "api.elevenlabs.io", captureContent: true);

        await synthesizer.SpeakAsync(Request(), Ct);

        var span = Span();
        var input = Json(span, "gen_ai.input.messages")[0];
        Assert.Equal("user", input.GetProperty("role").GetString());
        Assert.Equal("Is anyone there?", input.GetProperty("parts")[0].GetProperty("content").GetString());

        var audio = Json(span, "gen_ai.output.messages")[0].GetProperty("parts")[0];
        Assert.Equal("blob", audio.GetProperty("type").GetString());
        Assert.Equal("audio", audio.GetProperty("modality").GetString());
        Assert.Equal("audio/mpeg", audio.GetProperty("mime_type").GetString());
        Assert.Equal("v1:Is anyone there?", Encoding.UTF8.GetString(Convert.FromBase64String(audio.GetProperty("content").GetString()!)));
    }

    [Fact]
    public async Task A_failed_line_marks_the_span_as_an_error()
    {
        var synthesizer = new TracedSpeechSynthesizer(new FailingSpeechSynthesizer(), "api.elevenlabs.io", captureContent: false);

        await Assert.ThrowsAsync<SpeechException>(() => synthesizer.SpeakAsync(Request(), Ct));

        var span = Span();
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(typeof(SpeechException).FullName, span.GetTagItem("error.type"));
    }

    private Activity Span() => Assert.Single(_spans);

    private static JsonElement[] Json(Activity span, string tag) =>
        JsonSerializer.Deserialize<JsonElement[]>((string)span.GetTagItem(tag)!)!;

    private sealed class FailingSpeechSynthesizer : ISpeechSynthesizer
    {
        public string Provider => "elevenlabs";

        public string Model => "eleven_v4";

        public SpeechRequest Prepare(string performance, string? voiceId) => new(performance, null, voiceId ?? "");

        public Task<SpokenLine> SpeakAsync(SpeechRequest request, CancellationToken ct) =>
            throw new SpeechException("ElevenLabs refused the line (400): nope");

        public Task<IReadOnlyList<SpeechVoice>> ListVoicesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SpeechVoice>>([]);

        public Task<IReadOnlyList<SpeechVoice>> SearchVoicesAsync(VoiceQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<SpeechVoice>>([]);
    }
}
