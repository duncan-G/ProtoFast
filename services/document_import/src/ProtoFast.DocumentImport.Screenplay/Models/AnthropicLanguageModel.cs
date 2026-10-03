using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace ProtoFast.DocumentImport.Screenplay.Models;

public sealed class AnthropicLanguageModel(string modelId, ProviderOptions provider, int maxOutputTokens) : ILanguageModel
{
    private readonly AnthropicClient _client = new()
    {
        ApiKey = provider.ApiKey ?? throw new InvalidOperationException("Providers:anthropic:ApiKey is not configured."),
    };

    public string ModelId => modelId;

    public async Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = modelId,
            MaxTokens = maxOutputTokens,
            System = system,
            Messages = [new() { Role = Role.User, Content = user }],
        }, ct);

        return Reply(response);
    }

    public async Task<LanguageModelReply> ConverseAsync(
        string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = modelId,
            MaxTokens = maxOutputTokens,
            System = system,
            Messages = messages.Select(ToParam).ToList(),
            Tools = tools.Select(ToTool).ToList(),

            // An agent loop resends the whole conversation every turn.
            CacheControl = new CacheControlEphemeral(),
        }, ct);

        return Reply(response);
    }

    private LanguageModelReply Reply(Message response)
    {
        var stopReason = response.StopReason?.Raw();
        if (stopReason == "refusal")
        {
            throw new LanguageModelRefusedException(modelId, response.StopDetails?.Explanation);
        }

        var blocks = response.Content.Select(b => b.Value).ToList();
        var text = string.Concat(blocks.OfType<TextBlock>().Select(t => t.Text));
        var calls = blocks.OfType<ToolUseBlock>()
            .Select(b => new ToolCall(b.ID, b.Name, JsonSerializer.SerializeToElement(b.Input)))
            .ToList();

        var usage = response.Usage;
        var written = usage.CacheCreationInputTokens ?? 0;
        var read = usage.CacheReadInputTokens ?? 0;

        // Cache writes cost 1.25x input and reads 0.1x.
        var billedInput = usage.InputTokens + (long)(written * 1.25) + (long)(read * 0.1);
        return new LanguageModelReply(
            text, modelId, usage.InputTokens + written + read, usage.OutputTokens,
            provider.PriceOf(modelId, billedInput, usage.OutputTokens),
            response.ID, response.Model.Raw(), FinishReason(stopReason), calls);
    }

    private static MessageParam ToParam(ChatMessage message)
    {
        var content = new List<ContentBlockParam>();
        if (!string.IsNullOrEmpty(message.Text))
        {
            content.Add(new TextBlockParam(message.Text));
        }

        foreach (var call in message.ToolCalls)
        {
            content.Add(new ToolUseBlockParam
            {
                ID = call.Id,
                Name = call.Name,
                Input = call.Input.Deserialize<Dictionary<string, JsonElement>>() ?? [],
            });
        }

        foreach (var result in message.ToolResults)
        {
            content.Add(new ToolResultBlockParam(result.CallId) { Content = result.Content, IsError = result.IsError });
        }

        return new MessageParam { Role = message.Role == ChatRole.User ? Role.User : Role.Assistant, Content = content };
    }

    private static ToolUnion ToTool(ToolDefinition tool) => new Tool
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = new InputSchema
        {
            Properties = tool.InputSchema.GetProperty("properties").Deserialize<Dictionary<string, JsonElement>>(),
            Required = tool.InputSchema.TryGetProperty("required", out var required)
                ? required.Deserialize<List<string>>()
                : null,
        },
    };

    private static string? FinishReason(string? stopReason) => stopReason switch
    {
        null => null,
        "end_turn" or "stop_sequence" or "pause_turn" => "stop",
        "max_tokens" or "model_context_window_exceeded" => "length",
        "tool_use" => "tool_call",
        "refusal" => "content_filter",
        _ => stopReason,
    };
}
