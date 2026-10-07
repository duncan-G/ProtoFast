using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Engine.Storage;

public sealed record RecordedDecision(Decision Decision, DateTimeOffset RecordedAt);
