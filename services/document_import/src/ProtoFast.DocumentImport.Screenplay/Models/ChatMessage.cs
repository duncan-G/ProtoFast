namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <summary>A turn in a provider-neutral conversation; a user turn carries text or tool results, an assistant turn text and tool calls.</summary>
public sealed record ChatMessage(
    ChatRole Role, string? Text, IReadOnlyList<ToolCall> ToolCalls, IReadOnlyList<ToolResult> ToolResults)
{
    public static ChatMessage User(string text) => new(ChatRole.User, text, [], []);

    public static ChatMessage Assistant(string? text, IReadOnlyList<ToolCall> calls) => new(ChatRole.Assistant, text, calls, []);

    public static ChatMessage Results(IReadOnlyList<ToolResult> results) => new(ChatRole.User, null, [], results);
}
