using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Engine.Learning;

public sealed class NullDistiller(ILogger<NullDistiller> logger) : IDistiller
{
    public Task RequestCandidateAsync(PolicyRow row, CancellationToken ct)
    {
        logger.LogInformation(
            "Stage {Family}/{StageId} is ready for a candidate above {Tier}; no distiller is registered",
            row.Family, row.StageId, row.Primary);
        return Task.CompletedTask;
    }
}
