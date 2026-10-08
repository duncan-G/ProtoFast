using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Engine.Skills;

public sealed record ScriptSafetyVerdict(bool Safe, string Reason, IReadOnlyList<Finding> Findings, Cost Cost);
