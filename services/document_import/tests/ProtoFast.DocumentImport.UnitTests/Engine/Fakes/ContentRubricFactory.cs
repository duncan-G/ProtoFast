using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.UnitTests.Engine.Fakes;

internal sealed class ContentRubricFactory(EngineHarness harness) : IRubricVerifierFactory
{
    public IVerifier Create(VerifierSpec spec) => new ContentVerifier(spec.Id, deterministic: false, harness.Artifacts);
}
