namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>
/// Reads a script the agent wrote, after it compiles and before it is stored, for any reach beyond
/// what a script may touch. The agent is never told of the review, only of a refusal.
/// </summary>
public interface IScriptSafetyReviewer
{
    Task<ScriptSafetyVerdict> ReviewAsync(ScriptSafetyReview review, CancellationToken ct);
}
