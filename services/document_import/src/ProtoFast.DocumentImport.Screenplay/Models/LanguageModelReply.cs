namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <param name="FinishReason">An OpenTelemetry GenAI finish reason: stop, length, content_filter, tool_call or error.</param>
/// <param name="ToolCalls">The tools the model asked for, in order; only a conversation with tools has any.</param>
public sealed record LanguageModelReply(
    string Text,
    string ModelId,
    long InputTokens,
    long OutputTokens,
    decimal Cost,
    string? ResponseId = null,
    string? ResponseModel = null,
    string? FinishReason = null,
    IReadOnlyList<ToolCall>? ToolCalls = null);
