namespace ProtoFast.DocumentImport.Engine.Skills;

public readonly record struct SkillRef(string Id, int Version)
{
    public override string ToString() => $"{Id}@{Version}";
}
