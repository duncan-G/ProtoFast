namespace ProtoFast.DocumentImport.Engine.Briefing;

public enum StepEffectKind
{
    ContextRead,
    SkillLoaded,
    ArtifactRead,
    ArtifactWritten,
    Delegated,
    SkillPublished,
    PlaybookDefined,
    ExecutorDefined,
    VerifierDefined,
    DecisionRecorded,
    ScriptRan,
    Failed,
    SkillRemoved,
}
