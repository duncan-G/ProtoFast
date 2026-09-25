namespace ProtoFast.DocumentImport.Engine.Verification;

public interface IRubricVerifierFactory
{
    IVerifier Create(VerifierSpec spec);
}
