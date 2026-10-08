using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Engine.Families;

public sealed record FamilyVerifier(VerifierSpec Spec, DateTimeOffset AddedAt);
