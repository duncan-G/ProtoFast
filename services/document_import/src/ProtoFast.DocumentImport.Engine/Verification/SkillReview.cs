using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Verification;

/// <param name="Sources">Each script's C# source, by script name.</param>
/// <param name="Input">The input of the run publishing the skill.</param>
public sealed record SkillReview(
    Skill Skill, IReadOnlyDictionary<string, string> Sources, string RunId, DocumentSignature DocumentSignature, ArtifactRef Input);
