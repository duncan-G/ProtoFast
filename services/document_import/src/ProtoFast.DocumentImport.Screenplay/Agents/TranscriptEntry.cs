using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

/// <param name="Spent">What the model turn that produced an assistant message cost; null for the loop's own messages.</param>
public sealed record TranscriptEntry(ChatMessage Message, Cost? Spent);
