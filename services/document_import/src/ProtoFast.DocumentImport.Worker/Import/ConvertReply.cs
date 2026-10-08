namespace ProtoFast.DocumentImport.Worker.Import;

public sealed record ConvertReply(string MarkdownKey, int MarkdownBytes, IReadOnlyList<string>? Warnings);
