using Microsoft.Extensions.Logging;

namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <summary>
/// Sends a request to the same model class on another provider, once, when the primary refused it
/// or stayed unavailable past its retry window. Any other failure is the primary's to surface: a
/// second provider would hide it.
/// </summary>
public sealed class FallbackLanguageModel(ILanguageModel primary, ILanguageModel fallback, ILogger logger) : ILanguageModel
{
    public string ModelId => primary.ModelId;

    public async Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct)
    {
        try
        {
            return await primary.CompleteAsync(system, user, ct);
        }
        catch (Exception e) when (FallsBack(e))
        {
            logger.LogWarning(e, "{Primary} failed; falling back to {Fallback}", primary.ModelId, fallback.ModelId);
            return await fallback.CompleteAsync(system, user, ct);
        }
    }

    public async Task<LanguageModelReply> ConverseAsync(
        string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
    {
        try
        {
            return await primary.ConverseAsync(system, messages, tools, ct);
        }
        catch (Exception e) when (FallsBack(e))
        {
            logger.LogWarning(e, "{Primary} failed; falling back to {Fallback}", primary.ModelId, fallback.ModelId);
            return await fallback.ConverseAsync(system, messages, tools, ct);
        }
    }

    private static bool FallsBack(Exception e) => e is LanguageModelRefusedException or LanguageModelUnavailableException;
}
