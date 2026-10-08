using System.Diagnostics;
using System.Text.Json;
using ProtoFast.DocumentImport.Core;

namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <summary>
/// Wraps a model call in an OpenTelemetry GenAI <c>chat</c> client span, which the Aspire dashboard
/// renders as a conversation. Prompt and reply text are recorded only when content capture is on.
/// </summary>
public sealed class TracedLanguageModel(
    ILanguageModel inner, string providerName, string serverAddress, int maxOutputTokens, bool captureContent)
    : ILanguageModel
{
    private const string Operation = "chat";

    public string ModelId => inner.ModelId;

    public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct) =>
        TraceAsync(system, () => [new { role = "user", parts = Text(user) }], () => inner.CompleteAsync(system, user, ct));

    public Task<LanguageModelReply> ConverseAsync(
        string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
        TraceAsync(system, () => messages.Select(Message).ToArray(), () => inner.ConverseAsync(system, messages, tools, ct));

    private async Task<LanguageModelReply> TraceAsync(string system, Func<object[]> input, Func<Task<LanguageModelReply>> call)
    {
        using var activity = DocumentImportTelemetry.Source.StartActivity($"{Operation} {inner.ModelId}", ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("gen_ai.operation.name", Operation);
            activity.SetTag("gen_ai.provider.name", providerName);
            activity.SetTag("gen_ai.request.model", inner.ModelId);
            activity.SetTag("gen_ai.request.max_tokens", maxOutputTokens);
            activity.SetTag("server.address", serverAddress);
            activity.SetTag("server.port", 443);
            if (captureContent)
            {
                activity.SetTag("gen_ai.system_instructions", Json([new { type = "text", content = system }]));
                activity.SetTag("gen_ai.input.messages", Json(input()));
            }
        }

        try
        {
            var reply = await call();
            if (activity is { IsAllDataRequested: true })
            {
                activity.SetTag("gen_ai.response.id", reply.ResponseId);
                activity.SetTag("gen_ai.response.model", reply.ResponseModel ?? reply.ModelId);
                activity.SetTag("gen_ai.usage.input_tokens", reply.InputTokens);
                activity.SetTag("gen_ai.usage.output_tokens", reply.OutputTokens);
                if (reply.FinishReason is { } finish)
                {
                    activity.SetTag("gen_ai.response.finish_reasons", new[] { finish });
                }

                if (captureContent)
                {
                    activity.SetTag("gen_ai.output.messages", Json(
                    [
                        new
                        {
                            role = "assistant",
                            parts = Parts(reply.Text, reply.ToolCalls ?? [], []),
                            finish_reason = reply.FinishReason ?? "stop",
                        },
                    ]));
                }
            }

            return reply;
        }
        catch (Exception e)
        {
            activity.Fail(e);
            throw;
        }
    }

    private static object Message(ChatMessage message) => new
    {
        role = message.ToolResults.Count > 0 ? "tool" : message.Role == ChatRole.User ? "user" : "assistant",
        parts = Parts(message.Text, message.ToolCalls, message.ToolResults),
    };

    private static object[] Parts(string? text, IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolResult> results) =>
    [
        .. string.IsNullOrEmpty(text) ? [] : Text(text),
        .. calls.Select(c => (object)new { type = "tool_call", id = c.Id, name = c.Name, arguments = c.Input }),
        .. results.Select(r => (object)new { type = "tool_call_response", id = r.CallId, response = r.Content }),
    ];

    private static object[] Text(string content) => [new { type = "text", content }];

    private static string Json(object[] value) => JsonSerializer.Serialize(value);
}
