namespace ProtoFast.DocumentImport.Engine.Families;

public sealed record GenerationRuns(int Generation, int Runs, int OpenRuns, DateTimeOffset? LastRunAt);
