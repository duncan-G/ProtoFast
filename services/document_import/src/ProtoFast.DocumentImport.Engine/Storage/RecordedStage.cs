namespace ProtoFast.DocumentImport.Engine.Storage;

public sealed record RecordedStage(StageRecord Record, DateTimeOffset RecordedAt);
