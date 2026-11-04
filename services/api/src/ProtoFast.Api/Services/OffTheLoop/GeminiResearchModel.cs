using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Grpc.Core;
using Microsoft.Extensions.Options;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using Status = Grpc.Core.Status;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>
/// Gemini's medium model, grounded in Google Search and reading links with URL context. Gemini won't
/// search while held to a schema, so web research writes notes first and a second call shapes them.
/// </summary>
public sealed partial class GeminiResearchModel(
    IHttpClientFactory httpClients, IOptions<ModelProviders> providers, ModelCalls calls, ILogger<GeminiResearchModel> logger)
    : IResearchModel
{
    public const string HttpClientName = "gemini";

    private const string Provider = "gcp.gemini";

    // Thinking counts against it.
    private const int MaxOutputTokens = 32_768;

    private const int MaxSources = 12;

    private const string Notes = """
        Write up what you found as plain notes, not JSON: a line per field below, named as it is and written as its description asks, and each list as bullet points under its name. The fields:
        """;

    private const string Shape = """
        These are research notes. Copy them into JSON matching the schema, keeping their wording unless a field's description asks for a shorter form. Leave a field or list empty when the notes don't give it.
        """;

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async IAsyncEnumerable<ResearchUpdate> ResearchAsync(
        ResearchBrief brief, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var gemini = providers.Value.Gemini;
        if (string.IsNullOrEmpty(gemini.ApiKey))
        {
            throw new RpcException(new Status(StatusCode.Unavailable, LoopErrors.ResearchOff));
        }

        var answer = new StringBuilder();
        var sources = new List<WebSource>();
        if (brief.Web)
        {
            var notes = new StringBuilder();
            var search = Request(gemini, $"{brief.System}\n\n{Notes}\n{brief.Schema.GetRawText()}", brief.User);
            await foreach (var step in StreamAsync(gemini, search, Body(search, schema: null, web: true), notes, sources, cancellationToken))
            {
                yield return step;
            }

            var shape = Request(gemini, Shape, notes.ToString());
            var body = Body(shape, brief.Schema, web: false, thoughts: false);
            await foreach (var _ in StreamAsync(gemini, shape, body, answer, sources: [], cancellationToken))
            {
            }
        }
        else
        {
            var read = Request(gemini, brief.System, brief.User);
            await foreach (var step in StreamAsync(gemini, read, Body(read, brief.Schema, web: false), answer, sources, cancellationToken))
            {
                yield return step;
            }
        }

        using var parsed = JsonDocument.Parse(answer.ToString());
        yield return new ResearchFindings(parsed.RootElement.Clone(), sources.Take(MaxSources).ToList());
    }

    private static ChatRequest Request(ModelProvider gemini, string system, string user) =>
        new(Provider, new Uri(gemini.BaseUrl), gemini.Medium, MaxOutputTokens, system, user);

    private static object Body(ChatRequest request, JsonElement? schema, bool web, bool thoughts = true) => new
    {
        system_instruction = new { parts = new[] { new { text = request.System } } },
        contents = new[] { new { role = "user", parts = new[] { new { text = request.User } } } },
        tools = web ? new object[] { new { google_search = new { } }, new { url_context = new { } } } : null,
        generationConfig = new
        {
            responseMimeType = schema is null ? null : "application/json",
            responseJsonSchema = schema,
            thinkingConfig = thoughts ? new { includeThoughts = true } : null,
            maxOutputTokens = request.MaxOutputTokens,
        },
    };

    /// <summary>Fills the text and sources with the reply's, yielding what it shows of its work as it goes.</summary>
    private IAsyncEnumerable<ResearchProgress> StreamAsync(
        ModelProvider gemini, ChatRequest request, object body, StringBuilder text, List<WebSource> sources,
        CancellationToken cancellationToken) =>
        calls.StreamAsync(request, span => StreamAsync(gemini, request, span, body, text, sources, cancellationToken), cancellationToken);

    private async IAsyncEnumerable<ResearchProgress> StreamAsync(
        ModelProvider gemini, ChatRequest request, ChatSpan span, object body, StringBuilder text, List<WebSource> sources,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // A retried attempt starts over.
        text.Clear();
        sources.Clear();
        using var message = new HttpRequestMessage(
            HttpMethod.Post, $"{gemini.BaseUrl}/models/{request.Model}:streamGenerateContent?alt=sse");
        message.Headers.Add("x-goog-api-key", gemini.ApiKey);
        message.Content = JsonContent.Create(body, options: Json);
        using var response = await httpClients.CreateClient(HttpClientName)
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Gemini returned {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }

        var shown = new HashSet<string>();
        string? finishReason = null, responseId = null, responseModel = null;
        JsonElement? metadata = null;
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            using var chunk = JsonDocument.Parse(line[5..]);
            var root = chunk.RootElement;
            if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _))
            {
                throw Refused();
            }

            if (root.TryGetProperty("usageMetadata", out var usageMetadata))
            {
                metadata = usageMetadata.Clone();
            }

            responseId ??= root.TryGetProperty("responseId", out var id) ? id.GetString() : null;
            responseModel ??= root.TryGetProperty("modelVersion", out var version) ? version.GetString() : null;

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                continue;
            }

            var candidate = candidates[0];
            foreach (var step in Steps(candidate, text, sources))
            {
                if (shown.Add($"{step.Action}:{step.Subject}"))
                {
                    yield return step;
                }
            }

            if (candidate.TryGetProperty("finishReason", out var finish))
            {
                finishReason = finish.GetString();
            }
        }

        var usage = Usage(metadata);
        span.Finish(usage, text.ToString(), FinishReason(finishReason), responseId, responseModel);
        logger.LogInformation(
            "Researched with {Model}: {InputTokens} tokens in, {OutputTokens} out for {Cost} USD, stopped for {FinishReason}",
            request.Model, usage.InputTokens, usage.OutputTokens, gemini.PriceOf(request.Model, usage), finishReason);
        switch (finishReason)
        {
            case "STOP":
                break;
            case "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII":
                throw Refused();
            default:
                throw new InvalidOperationException($"Gemini stopped for {finishReason ?? "nothing"} before answering.");
        }
    }

    /// <summary>Thinking bills as output, and what search and URL context brought in as input.</summary>
    private static ModelUsage Usage(JsonElement? metadata)
    {
        long Count(string name) => metadata is { } m && m.TryGetProperty(name, out var count) ? count.GetInt64() : 0;
        return new ModelUsage(
            Count("promptTokenCount") + Count("toolUsePromptTokenCount"),
            Count("cachedContentTokenCount"),
            Count("candidatesTokenCount") + Count("thoughtsTokenCount"));
    }

    private static string? FinishReason(string? finishReason) => finishReason switch
    {
        null => null,
        "STOP" => "stop",
        "MAX_TOKENS" => "length",
        "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" => "content_filter",
        _ => finishReason.ToLowerInvariant(),
    };

    /// <summary>Collects the text and sources from a chunk, and what it shows of the work so far.</summary>
    private static List<ResearchProgress> Steps(JsonElement candidate, StringBuilder text, List<WebSource> sources)
    {
        var steps = new List<ResearchProgress>();
        if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                var piece = part.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                if (!part.TryGetProperty("thought", out var thought) || !thought.GetBoolean())
                {
                    text.Append(piece);
                }
                else if (ThoughtTitle().Match(piece) is { Success: true } title)
                {
                    steps.Add(new ResearchProgress(ResearchAction.Thinking, title.Groups[1].Value.Trim()));
                }
            }
        }

        if (candidate.TryGetProperty("groundingMetadata", out var grounding))
        {
            if (grounding.TryGetProperty("webSearchQueries", out var queries))
            {
                steps.AddRange(queries.EnumerateArray()
                    .Select(query => query.GetString())
                    .Where(query => !string.IsNullOrWhiteSpace(query))
                    .Select(query => new ResearchProgress(ResearchAction.Searching, query!)));
            }

            if (grounding.TryGetProperty("groundingChunks", out var chunks))
            {
                foreach (var chunk in chunks.EnumerateArray())
                {
                    if (chunk.TryGetProperty("web", out var web)
                        && web.TryGetProperty("uri", out var uri)
                        && uri.GetString() is { } url)
                    {
                        Add(sources, web.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "", url);
                    }
                }
            }
        }

        if (candidate.TryGetProperty("urlContextMetadata", out var urlContext)
            && urlContext.TryGetProperty("urlMetadata", out var pages))
        {
            foreach (var page in pages.EnumerateArray())
            {
                if (!page.TryGetProperty("retrievedUrl", out var retrieved) || retrieved.GetString() is not { } url)
                {
                    continue;
                }

                steps.Add(new ResearchProgress(ResearchAction.Reading, url));
                if (page.TryGetProperty("urlRetrievalStatus", out var status)
                    && status.GetString() == "URL_RETRIEVAL_STATUS_SUCCESS")
                {
                    Add(sources, "", url);
                }
            }
        }

        return steps;
    }

    /// <summary>Only http(s) pages, one per title; Google titles its search sources by site.</summary>
    private static void Add(List<WebSource> sources, string title, string url)
    {
        if (url.Length > OffTheLoopLimits.Url
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        var label = title.Trim();
        var source = new WebSource
        {
            Title = label.Length is > 0 and <= OffTheLoopLimits.Label ? label : LoopText.Site(uri),
            Url = url,
        };
        if (!sources.Any(s => s.Url == url || s.Title.Equals(source.Title, StringComparison.OrdinalIgnoreCase)))
        {
            sources.Add(source);
        }
    }

    private static RpcException Refused() => new(new Status(StatusCode.FailedPrecondition, LoopErrors.ResearchRefused));

    // A thought summary opens with its subject in bold.
    [GeneratedRegex(@"^\s*\*\*(.+?)\*\*")]
    private static partial Regex ThoughtTitle();
}
