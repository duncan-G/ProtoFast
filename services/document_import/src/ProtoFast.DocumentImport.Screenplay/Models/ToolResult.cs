namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <param name="Name">The tool's name; Gemini matches results to calls by it.</param>
public sealed record ToolResult(string CallId, string Name, string Content, bool IsError);
