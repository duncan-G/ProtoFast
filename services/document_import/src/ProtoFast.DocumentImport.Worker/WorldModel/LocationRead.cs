using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>A location described by the model: what it was asked, what it answered, and the answer parsed.</summary>
public sealed record LocationRead(string Prompt, LanguageModelReply Reply, LocationDescription Description);
