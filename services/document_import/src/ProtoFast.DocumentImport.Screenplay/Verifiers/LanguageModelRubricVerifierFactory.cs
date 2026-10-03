using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

public sealed class LanguageModelRubricVerifierFactory(
    ILanguageModelFactory models,
    IArtifactStore artifacts,
    LanguageModelOptions options,
    ILogger<RubricVerifier> logger) : IRubricVerifierFactory
{
    public IVerifier Create(VerifierSpec spec) =>
        new RubricVerifier(spec, models.For(ModelClasses.Medium), artifacts, options, logger);
}
