namespace ProtoFast.DocumentImport.Screenplay.Models;

public interface ILanguageModel
{
    string ModelId { get; }

    Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct);

    /// <summary>One model turn of a tool-using conversation; the caller runs the tools and continues it.</summary>
    Task<LanguageModelReply> ConverseAsync(
        string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct);
}
