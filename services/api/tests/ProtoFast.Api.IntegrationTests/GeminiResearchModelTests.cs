using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Api.Services.OffTheLoop;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public sealed class GeminiResearchModelTests : IDisposable
{
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new { type = "object" });

    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly ActivityListener _listener;

    public GeminiResearchModelTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ChatSpan.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task Web_research_streams_its_work_then_shapes_its_notes()
    {
        var gemini = new RecordedGemini(
            [
                """{"candidates":[{"content":{"parts":[{"text":"**Checking funding**\n\nI'm looking.","thought":true}]}}]}""",
                """{"candidates":[{"content":{"parts":[{"text":"**Checking funding**\n\nStill.","thought":true},{"text":"headcount: "}]}}]}""",
                """{"candidates":[{"content":{"parts":[{"text":"~120"}]},"finishReason":"STOP","groundingMetadata":{"webSearchQueries":["linear funding"],"groundingChunks":[{"web":{"uri":"https://vertexaisearch.example/r/1","title":"techcrunch.com"}},{"web":{"uri":"javascript:alert(1)","title":"x"}}]},"urlContextMetadata":{"urlMetadata":[{"retrievedUrl":"https://www.linear.app/careers","urlRetrievalStatus":"URL_RETRIEVAL_STATUS_SUCCESS"},{"retrievedUrl":"https://linear.app/private","urlRetrievalStatus":"URL_RETRIEVAL_STATUS_PAYWALL"}]}}],"usageMetadata":{"promptTokenCount":40}}""",
            ],
            [
                """{"candidates":[{"content":{"parts":[{"text":"{\"headcount\":"}]}}]}""",
                """{"candidates":[{"content":{"parts":[{"text":"\"~120\"}"}]},"finishReason":"STOP"}]}""",
            ]);

        var updates = await Research(gemini, new ResearchBrief("system", "user", Schema, Web: true));

        Assert.Equal(
            [
                (ResearchAction.Thinking, "Checking funding"),
                (ResearchAction.Searching, "linear funding"),
                (ResearchAction.Reading, "https://www.linear.app/careers"),
                (ResearchAction.Reading, "https://linear.app/private"),
            ],
            updates.OfType<ResearchProgress>().Select(p => (p.Action, p.Subject)));
        var findings = Assert.IsType<ResearchFindings>(updates.Last());
        Assert.Equal("~120", findings.Answer.GetProperty("headcount").GetString());
        Assert.Equal(
            [("techcrunch.com", "https://vertexaisearch.example/r/1"), ("linear.app", "https://www.linear.app/careers")],
            findings.Sources.Select(s => (s.Title, s.Url)));

        Assert.EndsWith("/models/gemini-medium:streamGenerateContent?alt=sse", gemini.Requests[0].RequestUri!.ToString());
        Assert.Equal("test-key", gemini.Requests[0].Headers.GetValues("x-goog-api-key").Single());
        using var search = JsonDocument.Parse(gemini.Bodies[0]);
        Assert.Equal(2, search.RootElement.GetProperty("tools").GetArrayLength());
        Assert.False(search.RootElement.GetProperty("generationConfig").TryGetProperty("responseJsonSchema", out _));
        Assert.EndsWith(
            "{\"type\":\"object\"}",
            search.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString());
        using var shape = JsonDocument.Parse(gemini.Bodies[1]);
        Assert.False(shape.RootElement.TryGetProperty("tools", out _));
        Assert.Equal("object", shape.RootElement.GetProperty("generationConfig").GetProperty("responseJsonSchema").GetProperty("type").GetString());
        Assert.Equal("headcount: ~120", shape.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Without_the_web_one_call_answers_to_the_schema()
    {
        var gemini = new RecordedGemini(["""{"candidates":[{"content":{"parts":[{"text":"{}"}]},"finishReason":"STOP"}]}"""]);

        await Research(gemini, new ResearchBrief("system", "user", Schema, Web: false));

        using var body = JsonDocument.Parse(Assert.Single(gemini.Bodies));
        Assert.False(body.RootElement.TryGetProperty("tools", out _));
        Assert.True(body.RootElement.GetProperty("generationConfig").TryGetProperty("responseJsonSchema", out _));
    }

    [Fact]
    public async Task An_outage_is_waited_out_each_attempt_in_its_own_gen_ai_span()
    {
        var gemini = new RecordedGemini(
        [
            """{"responseId":"r-1","modelVersion":"gemini-medium-001","candidates":[{"content":{"parts":[{"text":"{}"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":100,"cachedContentTokenCount":40,"toolUsePromptTokenCount":5,"candidatesTokenCount":20,"thoughtsTokenCount":30}}""",
        ]);
        gemini.Outages.Enqueue(HttpStatusCode.ServiceUnavailable);
        using var test = new Activity(nameof(An_outage_is_waited_out_each_attempt_in_its_own_gen_ai_span)).Start();

        var updates = await Research(gemini, new ResearchBrief("system", "user", Schema, Web: false));

        Assert.IsType<ResearchFindings>(updates.Last());
        Assert.Equal(2, gemini.Requests.Count);
        var spans = _spans.Where(s => s.TraceId == test.TraceId).ToList();
        Assert.Equal(2, spans.Count);
        Assert.All(spans, s => Assert.Equal("chat gemini-medium", s.DisplayName));
        Assert.Equal(ActivityStatusCode.Error, spans[0].Status);
        Assert.Equal(typeof(HttpRequestException).FullName, spans[0].GetTagItem("error.type"));

        var span = spans[1];
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        Assert.Equal("chat", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("gcp.gemini", span.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("generativelanguage.googleapis.com", span.GetTagItem("server.address"));
        Assert.Equal("r-1", span.GetTagItem("gen_ai.response.id"));
        Assert.Equal("gemini-medium-001", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal(105L, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(50L, span.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Equal(["stop"], (string[])span.GetTagItem("gen_ai.response.finish_reasons")!);
        Assert.Null(span.GetTagItem("gen_ai.input.messages"));
    }

    [Fact]
    public async Task A_request_the_provider_rejects_is_not_retried()
    {
        var gemini = new RecordedGemini();
        gemini.Outages.Enqueue(HttpStatusCode.BadRequest);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Research(gemini, new ResearchBrief("system", "user", Schema, Web: false)));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Single(gemini.Requests);
    }

    [Fact]
    public async Task A_blocked_answer_reads_as_refused()
    {
        var gemini = new RecordedGemini(["""{"candidates":[{"finishReason":"SAFETY"}]}"""]);

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Research(gemini, new ResearchBrief("system", "user", Schema, Web: true)));

        Assert.Equal(LoopErrors.ResearchRefused, error.Status.Detail);
    }

    [Fact]
    public async Task Without_a_key_research_is_off()
    {
        var gemini = new RecordedGemini();

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Research(gemini, new ResearchBrief("system", "user", Schema, Web: true), key: null));

        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        Assert.Equal(LoopErrors.ResearchOff, error.Status.Detail);
        Assert.Empty(gemini.Requests);
    }

    private static async Task<List<ResearchUpdate>> Research(
        RecordedGemini gemini, ResearchBrief brief, string? key = "test-key")
    {
        var providers = new ModelProviders
        {
            Retry = new ModelRetryOptions
            {
                InitialDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(1),
                MaxWait = TimeSpan.FromMilliseconds(5),
            },
        };
        providers.Gemini.ApiKey = key;
        providers.Gemini.Medium = "gemini-medium";
        var options = Options.Create(providers);
        var calls = new ModelCalls(options, TimeProvider.System, NullLogger<ModelCalls>.Instance);
        var model = new GeminiResearchModel(gemini, options, calls, NullLogger<GeminiResearchModel>.Instance);

        var updates = new List<ResearchUpdate>();
        await foreach (var update in model.ResearchAsync(brief, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        return updates;
    }
}
