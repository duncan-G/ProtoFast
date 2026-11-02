namespace ProtoFast.DocumentImport.Core;

/// <summary>The conversion worker's outcome: the Markdown's key, or the code of why the upload never will convert.</summary>
public sealed record ConversionResult(string? MarkdownKey, string? Error);
