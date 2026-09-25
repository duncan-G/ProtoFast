namespace ProtoFast.DocumentImport.Engine;

public enum OutcomeKind
{
    VerifierPass, VerifierFail,
    ShadowPass, ShadowFail,
    ExternalCorrection,
    WorkflowShadowPass, WorkflowShadowFail
}
