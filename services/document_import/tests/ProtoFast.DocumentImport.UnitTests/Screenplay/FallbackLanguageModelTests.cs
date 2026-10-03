using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoFast.DocumentImport.Screenplay.Models;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

public sealed class FallbackLanguageModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_refusal_goes_to_the_fallback()
    {
        var fallback = new FixedModel("gemini-3.1-pro-preview", () => Reply("gemini-3.1-pro-preview"));
        var model = new FallbackLanguageModel(
            new FixedModel("claude-opus-5", () => throw new LanguageModelRefusedException("claude-opus-5", "cyber")),
            fallback, NullLogger.Instance);

        var reply = await model.CompleteAsync("s", "u", Ct);

        Assert.Equal("gemini-3.1-pro-preview", reply.ModelId);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Other_failures_do_not_fall_back()
    {
        var fallback = new FixedModel("gemini-3.1-pro-preview", () => Reply("gemini-3.1-pro-preview"));
        var model = new FallbackLanguageModel(
            new FixedModel("claude-opus-5", () => throw new HttpRequestException("overloaded")),
            fallback, NullLogger.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => model.CompleteAsync("s", "u", Ct));
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task A_primary_that_stayed_unavailable_goes_to_the_fallback()
    {
        var fallback = new FixedModel("gemini-3.1-pro-preview", () => Reply("gemini-3.1-pro-preview"));
        var model = new FallbackLanguageModel(
            new FixedModel("claude-opus-5", () => throw new LanguageModelUnavailableException(
                "claude-opus-5", 5, TimeSpan.FromMinutes(10), new HttpRequestException("overloaded"))),
            fallback, NullLogger.Instance);

        var reply = await model.ConverseAsync("s", [ChatMessage.User("u")], [], Ct);

        Assert.Equal("gemini-3.1-pro-preview", reply.ModelId);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task A_refused_fallback_surfaces_its_refusal()
    {
        var model = new FallbackLanguageModel(
            new FixedModel("claude-opus-5", () => throw new LanguageModelRefusedException("claude-opus-5", "cyber")),
            new FixedModel("gemini-3.1-pro-preview", () => throw new LanguageModelRefusedException("gemini-3.1-pro-preview", "SAFETY")),
            NullLogger.Instance);

        var refused = await Assert.ThrowsAsync<LanguageModelRefusedException>(() => model.CompleteAsync("s", "u", Ct));
        Assert.Equal("gemini-3.1-pro-preview", refused.ModelId);
    }

    [Theory]
    [InlineData("""{ "promptFeedback": { "blockReason": "PROHIBITED_CONTENT" } }""")]
    [InlineData("""{ "candidates": [{ "finishReason": "SAFETY" }] }""")]
    public async Task Gemini_blocks_are_refusals(string body)
    {
        var model = new GeminiLanguageModel(
            "gemini-3.1-pro-preview", new ProviderOptions { ApiKey = "key" }, new HttpClient(new FixedHandler(body)), 1_000);

        await Assert.ThrowsAsync<LanguageModelRefusedException>(() => model.CompleteAsync("s", "u", Ct));
    }

    [Fact]
    public async Task A_refused_conversation_turn_goes_to_the_fallback()
    {
        var fallback = new FixedModel("gemini-3.1-pro-preview", () => Reply("gemini-3.1-pro-preview"));
        var model = new FallbackLanguageModel(
            new FixedModel("claude-opus-5", () => throw new LanguageModelRefusedException("claude-opus-5", "cyber")),
            fallback, NullLogger.Instance);

        var reply = await model.ConverseAsync("s", [ChatMessage.User("u")], [], Ct);

        Assert.Equal("gemini-3.1-pro-preview", reply.ModelId);
    }

    [Fact]
    public async Task Gemini_tool_calls_come_back_with_their_signatures()
    {
        var handler = new FixedHandler("""
            {
              "candidates": [{
                "content": { "parts": [
                  { "text": "Loading it." },
                  { "functionCall": { "name": "execute_skill", "args": { "skill": "context" } }, "thoughtSignature": "sig-1" }
                ] },
                "finishReason": "STOP"
              }],
              "usageMetadata": { "promptTokenCount": 10, "candidatesTokenCount": 5 }
            }
            """);
        var model = new GeminiLanguageModel(
            "gemini-3.1-pro-preview", new ProviderOptions { ApiKey = "key" }, new HttpClient(handler), 1_000);
        var tool = new ToolDefinition("execute_skill", "Load a skill.", JsonDocument.Parse("""{ "type": "object", "properties": {} }""").RootElement);

        var reply = await model.ConverseAsync("s", [ChatMessage.User("u")], [tool], Ct);
        var call = Assert.Single(reply.ToolCalls!);
        Assert.Equal(("execute_skill", "context", "sig-1", "tool_call"),
            (call.Name, call.Input.GetProperty("skill").GetString(), call.Signature, reply.FinishReason));

        await model.ConverseAsync(
            "s",
            [ChatMessage.User("u"), ChatMessage.Assistant(reply.Text, [call]),
             ChatMessage.Results([new ToolResult(call.Id, call.Name, "loaded", IsError: false)])],
            [tool], Ct);
        var contents = JsonDocument.Parse(handler.LastRequest!).RootElement.GetProperty("contents");
        Assert.Equal("sig-1", contents[1].GetProperty("parts")[1].GetProperty("thoughtSignature").GetString());
        Assert.Equal("execute_skill", contents[2].GetProperty("parts")[0].GetProperty("functionResponse").GetProperty("name").GetString());
    }

    private static LanguageModelReply Reply(string modelId) => new("{}", modelId, 1, 1, 0);

    private sealed class FixedModel(string modelId, Func<LanguageModelReply> reply) : ILanguageModel
    {
        public int Calls { get; private set; }

        public string ModelId => modelId;

        public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(reply());
        }

        public Task<LanguageModelReply> ConverseAsync(
            string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
            CompleteAsync(system, "", ct);
    }

    private sealed class FixedHandler(string body) : HttpMessageHandler
    {
        public string? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
