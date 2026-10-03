using System.Net.Http.Json;
using System.Text.Json;

namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <summary>Gemini's generateContent endpoint over plain HTTP.</summary>
public sealed class GeminiLanguageModel(string modelId, ProviderOptions provider, HttpClient http, int maxOutputTokens) : ILanguageModel
{
    public const string HttpClientName = "gemini";

    // Gemini's documented stand-in for a call it did not sign, such as one made by another provider.
    private const string UnsignedCall = "skip_thought_signature_validator";

    private static readonly JsonElement NoArgs = JsonSerializer.SerializeToElement(new { });

    public string ModelId => modelId;

    public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct) =>
        SendAsync(new
        {
            system_instruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = user } } } },
            generationConfig = new { responseMimeType = "application/json", maxOutputTokens },
        }, ct);

    public Task<LanguageModelReply> ConverseAsync(
        string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
        SendAsync(new
        {
            system_instruction = new { parts = new[] { new { text = system } } },
            contents = messages.Select(ToContent).ToList(),
            tools = new[]
            {
                new
                {
                    functionDeclarations = tools
                        .Select(t => new { name = t.Name, description = t.Description, parametersJsonSchema = t.InputSchema })
                        .ToList(),
                },
            },
            generationConfig = new { maxOutputTokens },
        }, ct);

    private async Task<LanguageModelReply> SendAsync(object body, CancellationToken ct)
    {
        var key = provider.ApiKey ?? throw new InvalidOperationException("Providers:gemini:ApiKey is not configured.");
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent");
        request.Headers.Add("x-goog-api-key", key);
        request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini returned {(int)response.StatusCode}: {text}", null, response.StatusCode);
        }

        using var json = JsonDocument.Parse(text);
        return Reply(json.RootElement);
    }

    private LanguageModelReply Reply(JsonElement root)
    {
        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var blocked))
        {
            throw new LanguageModelRefusedException(modelId, blocked.GetString());
        }

        var candidate = root.GetProperty("candidates")[0];
        var finishReason = candidate.TryGetProperty("finishReason", out var finish) ? FinishReason(finish.GetString()) : null;
        if (finishReason == "content_filter")
        {
            throw new LanguageModelRefusedException(modelId, finish.GetString());
        }

        var parts = candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var p)
            ? p.EnumerateArray().ToList()
            : [];
        var text = string.Concat(parts.Select(part => part.TryGetProperty("text", out var t) ? t.GetString() : null));
        var calls = parts
            .Where(part => part.TryGetProperty("functionCall", out _))
            .Select(part =>
            {
                var call = part.GetProperty("functionCall");
                return new ToolCall(
                    call.TryGetProperty("id", out var id) ? id.GetString()! : $"call-{Guid.NewGuid():N}",
                    call.GetProperty("name").GetString()!,
                    call.TryGetProperty("args", out var args) ? args.Clone() : NoArgs,
                    part.TryGetProperty("thoughtSignature", out var signature) ? signature.GetString() : null);
            })
            .ToList();

        long input = 0, output = 0;
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            input = usage.TryGetProperty("promptTokenCount", out var i) ? i.GetInt64() : 0;
            output = usage.TryGetProperty("candidatesTokenCount", out var o) ? o.GetInt64() : 0;
        }

        return new LanguageModelReply(
            text, modelId, input, output, provider.PriceOf(modelId, input, output),
            root.TryGetProperty("responseId", out var responseId) ? responseId.GetString() : null,
            root.TryGetProperty("modelVersion", out var version) ? version.GetString() : null,
            calls.Count > 0 ? "tool_call" : finishReason,
            calls);
    }

    private static object ToContent(ChatMessage message)
    {
        var parts = new List<object>();
        if (!string.IsNullOrEmpty(message.Text))
        {
            parts.Add(new { text = message.Text });
        }

        parts.AddRange(message.ToolCalls.Select(call => new
        {
            functionCall = new { name = call.Name, args = call.Input },
            thoughtSignature = call.Signature ?? UnsignedCall,
        }));
        parts.AddRange(message.ToolResults.Select(result => new
        {
            functionResponse = new
            {
                name = result.Name,
                response = result.IsError ? (object)new { error = result.Content } : new { content = result.Content },
            },
        }));

        return new { role = message.Role == ChatRole.User ? "user" : "model", parts };
    }

    private static string? FinishReason(string? finishReason) => finishReason switch
    {
        null => null,
        "STOP" => "stop",
        "MAX_TOKENS" => "length",
        "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" or "IMAGE_SAFETY" => "content_filter",
        "MALFORMED_FUNCTION_CALL" => "error",
        _ => finishReason.ToLowerInvariant(),
    };
}
