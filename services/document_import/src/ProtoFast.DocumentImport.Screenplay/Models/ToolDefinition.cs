using System.Text.Json;

namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <param name="InputSchema">A JSON Schema object for the tool's input.</param>
public sealed record ToolDefinition(string Name, string Description, JsonElement InputSchema);
