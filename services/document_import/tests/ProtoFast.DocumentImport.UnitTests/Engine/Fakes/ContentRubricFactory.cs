using ProtoFast.DocumentImport.Engine;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

internal sealed class ContentRubricFactory(EngineHarness harness) : IRubricVerifierFactory
{
    public IVerifier Create(VerifierSpec spec) => new ContentVerifier(spec.Id, deterministic: false, harness.Artifacts);
}
