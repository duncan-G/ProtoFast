using Microsoft.Extensions.Logging;

namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// The distiller until a model-backed one is registered: it logs the request and produces
/// nothing, so a ready row simply stays at its tier.
/// </summary>
public sealed class NullDistiller(ILogger<NullDistiller> logger) : IDistiller
{
    public Task RequestCandidateAsync(PolicyRow row, CancellationToken ct)
    {
        logger.LogInformation(
            "Stage {Bucket}/{StageId} is ready for a candidate above {Tier}; no distiller is registered",
            row.Bucket, row.StageId, row.Tier);
        return Task.CompletedTask;
    }
}
