using System.Runtime.CompilerServices;
using ProtoFast.Api.Services.OffTheLoop;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Writes the given pieces, then throws when told to fail, or waits to be stopped when told to hold.</summary>
public sealed class ScriptedWritingModel(params string[] pieces) : IWritingModel
{
    public string? User { get; private set; }

    public bool Fails { get; init; }

    public bool Holds { get; init; }

    public async IAsyncEnumerable<string> WriteAsync(
        string system, string user, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        User = user;
        foreach (var piece in pieces)
        {
            await Task.Yield();
            yield return piece;
        }

        if (Fails)
        {
            throw new HttpRequestException("overloaded");
        }

        if (Holds)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
