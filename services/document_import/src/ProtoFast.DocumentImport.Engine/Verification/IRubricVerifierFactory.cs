namespace ProtoFast.DocumentImport.Engine;

public interface IRubricVerifierFactory
{
    IVerifier Create(VerifierSpec spec);
}
