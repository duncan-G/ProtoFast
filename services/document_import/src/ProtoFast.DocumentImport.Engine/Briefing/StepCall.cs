namespace ProtoFast.DocumentImport.Engine.Briefing;

/// <param name="Script">Null when the call loaded the skill rather than running one of its scripts.</param>
/// <param name="Effects">Empty for a step recorded from the transcript after the fact.</param>
public sealed record StepCall(
    string CallId, string Tool, string? Skill, string? Script, bool IsError, IReadOnlyList<StepEffect> Effects)
{
    public string Headline => Effects.Count > 0
        ? string.Join("; ", Effects.Select(e => e.Summary))
        : (Script is null ? $"Loaded {Skill}" : $"Ran {Skill}/{Script}") + (IsError ? " (error)" : "");
}
