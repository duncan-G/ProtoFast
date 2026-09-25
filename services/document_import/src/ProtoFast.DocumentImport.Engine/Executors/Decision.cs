namespace ProtoFast.DocumentImport.Engine.Executors;

public sealed record Decision(string Key, string Choice, string Rationale, double Confidence);
