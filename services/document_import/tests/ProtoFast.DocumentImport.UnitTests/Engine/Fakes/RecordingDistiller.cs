using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

internal sealed class RecordingDistiller : IDistiller
{
    public ConcurrentQueue<PolicyRow> Requests { get; } = new();

    public Task RequestCandidateAsync(PolicyRow row, CancellationToken ct)
    {
        Requests.Enqueue(row);
        return Task.CompletedTask;
    }
}
