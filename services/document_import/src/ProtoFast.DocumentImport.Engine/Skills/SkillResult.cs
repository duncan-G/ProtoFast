namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>An error is the agent's to fix, so it goes back to the agent instead of failing the run.</summary>
public sealed record SkillResult(string Content, bool IsError)
{
    public static SkillResult Ok(string content) => new(content, false);

    public static SkillResult Error(string message) => new(message, true);
}
