using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>A scene read by the model: what it was asked, what it answered, and the answer parsed.</summary>
public sealed record SceneRead(string Prompt, LanguageModelReply Reply, SceneReading Reading, TimeSpan Duration);
