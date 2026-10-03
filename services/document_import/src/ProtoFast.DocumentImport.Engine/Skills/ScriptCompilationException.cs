namespace ProtoFast.DocumentImport.Engine.Skills;

public sealed class ScriptCompilationException(IReadOnlyList<string> errors)
    : ArgumentException("The script does not compile:\n" + string.Join('\n', errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
