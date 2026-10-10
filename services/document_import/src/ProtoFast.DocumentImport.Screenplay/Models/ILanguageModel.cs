namespace ProtoFast.DocumentImport.Screenplay.Models;

public interface ILanguageModel
{
    string ModelId { get; }

    Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct);

    /// <summary>One turn that shows the model pictures, each after its caption, ahead of the text.</summary>
    Task<LanguageModelReply> CompleteAsync(string system, IReadOnlyList<ModelImage> images, string user, CancellationToken ct) =>
        images.Count == 0
            ? CompleteAsync(system, user, ct)
            : throw new NotSupportedException($"{ModelId} is not shown pictures.");

    /// <summary>One model turn of a tool-using conversation; the caller runs the tools and continues it.</summary>
    Task<LanguageModelReply> ConverseAsync(
        string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct);
}
