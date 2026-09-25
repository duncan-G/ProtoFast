namespace ProtoFast.DocumentImport.Engine;

public sealed record Decision(string Key, string Choice, string Rationale, double Confidence);
