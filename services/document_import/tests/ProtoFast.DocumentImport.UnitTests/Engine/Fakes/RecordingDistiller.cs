using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.UnitTests.Engine.Fakes;

internal sealed class RecordingDistiller : IDistiller
{
    public ConcurrentQueue<PolicyRow> Requests { get; } = new();

    public Task RequestCandidateAsync(PolicyRow row, CancellationToken ct)
    {
        Requests.Enqueue(row);
        return Task.CompletedTask;
    }
}
