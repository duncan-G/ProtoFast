using System.Text.Json;

namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <param name="Signature">Gemini's thought signature, which must go back with the call in the next request.</param>
public sealed record ToolCall(string Id, string Name, JsonElement Input, string? Signature = null);
