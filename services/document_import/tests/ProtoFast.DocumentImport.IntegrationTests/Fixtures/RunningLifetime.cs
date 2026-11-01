using Microsoft.Extensions.Hosting;

namespace ProtoFast.DocumentImport.IntegrationTests.Fixtures;

/// <summary>A host that never stops, for queue consumers outside a real host.</summary>
internal sealed class RunningLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication()
    {
    }
}
