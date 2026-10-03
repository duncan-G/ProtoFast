using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Screenplay.Models;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

public sealed class TracedLanguageModelTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly ActivityListener _listener;

    public TracedLanguageModelTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DocumentImportTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task A_call_is_a_gen_ai_chat_span()
    {
        var model = Traced(new FixedModel(new LanguageModelReply(
            "{}", "claude-opus-5", 120, 30, 0.01m, "msg_1", "claude-opus-5-20260101", "stop")), captureContent: false);

        await model.CompleteAsync("system", "user", Ct);

        var span = Span();
        Assert.Equal("chat claude-opus-5", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal("chat", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("anthropic", span.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("claude-opus-5", span.GetTagItem("gen_ai.request.model"));
        Assert.Equal("claude-opus-5-20260101", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal("msg_1", span.GetTagItem("gen_ai.response.id"));
        Assert.Equal(120L, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(30L, span.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Equal(["stop"], (string[])span.GetTagItem("gen_ai.response.finish_reasons")!);
        Assert.Null(span.GetTagItem("gen_ai.input.messages"));
    }

    [Fact]
    public async Task Captured_content_is_in_the_gen_ai_message_format()
    {
        var model = Traced(new FixedModel(new LanguageModelReply("the reply", "m", 1, 1, 0, FinishReason: "length")), captureContent: true);

        await model.CompleteAsync("the rules", "the manuscript", Ct);

        var span = Span();
        var system = Json(span, "gen_ai.system_instructions")[0];
        Assert.Equal("the rules", system.GetProperty("content").GetString());

        var input = Json(span, "gen_ai.input.messages")[0];
        Assert.Equal("user", input.GetProperty("role").GetString());
        Assert.Equal("the manuscript", input.GetProperty("parts")[0].GetProperty("content").GetString());

        var output = Json(span, "gen_ai.output.messages")[0];
        Assert.Equal("assistant", output.GetProperty("role").GetString());
        Assert.Equal("length", output.GetProperty("finish_reason").GetString());
        Assert.Equal("the reply", output.GetProperty("parts")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Captured_tool_calls_and_results_are_message_parts()
    {
        var call = new ToolCall("call_1", "execute_code", JsonDocument.Parse("""{ "skill": "context" }""").RootElement);
        var model = Traced(new FixedModel(new LanguageModelReply("", "m", 1, 1, 0, FinishReason: "tool_call", ToolCalls: [call])), captureContent: true);

        await model.ConverseAsync(
            "s", [ChatMessage.User("go"), ChatMessage.Results([new ToolResult("call_0", "execute_skill", "loaded", false)])], [], Ct);

        var span = Span();
        var input = Json(span, "gen_ai.input.messages");
        Assert.Equal("tool", input[1].GetProperty("role").GetString());
        Assert.Equal("tool_call_response", input[1].GetProperty("parts")[0].GetProperty("type").GetString());
        var output = Json(span, "gen_ai.output.messages")[0].GetProperty("parts")[0];
        Assert.Equal(("tool_call", "execute_code"), (output.GetProperty("type").GetString(), output.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_failed_call_is_an_error_span()
    {
        var model = Traced(new FixedModel(null), captureContent: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.CompleteAsync("s", "u", Ct));

        var span = Span();
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, span.GetTagItem("error.type"));
    }

    private static TracedLanguageModel Traced(ILanguageModel inner, bool captureContent) =>
        new(inner, "anthropic", "api.anthropic.com", 16_000, captureContent);

    private Activity Span() => Assert.Single(_spans, s => s.GetTagItem("gen_ai.operation.name") is not null);

    private static JsonElement Json(Activity span, string tag) =>
        JsonDocument.Parse((string)span.GetTagItem(tag)!).RootElement;

    private sealed class FixedModel(LanguageModelReply? reply) : ILanguageModel
    {
        public string ModelId => reply?.ModelId ?? "claude-opus-5";

        public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct) =>
            reply is null ? throw new InvalidOperationException("refused") : Task.FromResult(reply);

        public Task<LanguageModelReply> ConverseAsync(
            string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
            CompleteAsync(system, "", ct);
    }
}
