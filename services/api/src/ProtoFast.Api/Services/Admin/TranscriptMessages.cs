using System.Text.Json;
using ProtoFast.DocumentImport.Engine.Storage;
using Proto = ProtoFast.Api.Admin.Theplot;

namespace ProtoFast.Api.Services.Admin;

/// <summary>
/// Reads the journal entries the discovery agent writes (a <c>TranscriptEntry</c>: a chat message
/// and what the turn cost). The api does not reference the agent, so the shape is read here as
/// JSON; an entry it cannot read still shows up, as its raw text.
/// </summary>
public static class TranscriptMessages
{
    public static Proto.TranscriptMessage From(TranscriptMessage message)
    {
        var result = new Proto.TranscriptMessage
        {
            Sequence = message.Sequence,
            RecordedUnixMs = EngineMessages.Millis(message.RecordedAt),
        };

        try
        {
            using var document = JsonDocument.Parse(message.Json);
            var root = document.RootElement;
            if (root.TryGetProperty("message", out var chat))
            {
                Read(chat, result);
            }

            if (root.TryGetProperty("spent", out var spent) && spent.ValueKind == JsonValueKind.Object)
            {
                result.Spend = new Proto.Spend
                {
                    UsdMicros = spent.TryGetProperty("amount", out var amount) ? EngineMessages.Micros(amount.GetDecimal()) : 0,
                    DurationMs = spent.TryGetProperty("duration", out var duration)
                                 && TimeSpan.TryParse(duration.GetString(), out var span)
                        ? (long)span.TotalMilliseconds
                        : 0,
                };
            }
        }
        catch (JsonException)
        {
            result.Text = message.Json;
        }

        return result;
    }

    private static void Read(JsonElement chat, Proto.TranscriptMessage result)
    {
        result.Role = chat.TryGetProperty("role", out var role) ? role.GetString() switch
        {
            "User" => Proto.TranscriptRole.User,
            "Assistant" => Proto.TranscriptRole.Assistant,
            _ => Proto.TranscriptRole.Unspecified,
        } : Proto.TranscriptRole.Unspecified;
        result.Text = chat.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "";

        if (chat.TryGetProperty("toolCalls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in calls.EnumerateArray())
            {
                result.ToolCalls.Add(new Proto.TranscriptToolCall
                {
                    Id = Text(call, "id"),
                    Name = Text(call, "name"),
                    InputJson = call.TryGetProperty("input", out var input) ? input.GetRawText() : "",
                });
            }
        }

        if (chat.TryGetProperty("toolResults", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in results.EnumerateArray())
            {
                result.ToolResults.Add(new Proto.TranscriptToolResult
                {
                    CallId = Text(tool, "callId"),
                    Name = Text(tool, "name"),
                    Content = Text(tool, "content"),
                    IsError = tool.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True,
                });
            }
        }
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
