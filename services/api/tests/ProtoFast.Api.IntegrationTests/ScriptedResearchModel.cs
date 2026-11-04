using System.Runtime.CompilerServices;
using System.Text.Json;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Api.Services.OffTheLoop;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Reports the given steps, then answers with the given findings; told to fail it throws instead, told to hold it waits to be stopped.</summary>
public sealed class ScriptedResearchModel(object? answer, params (ResearchAction Action, string Subject)[] steps)
    : IResearchModel
{
    public ResearchBrief? Brief { get; private set; }

    public IReadOnlyList<WebSource> Sources { get; init; } = [];

    public bool Fails { get; init; }

    public bool Holds { get; init; }

    public async IAsyncEnumerable<ResearchUpdate> ResearchAsync(
        ResearchBrief brief, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Brief = brief;
        foreach (var (action, subject) in steps)
        {
            await Task.Yield();
            yield return new ResearchProgress(action, subject);
        }

        if (Fails)
        {
            throw new HttpRequestException("overloaded");
        }

        if (Holds)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        yield return new ResearchFindings(JsonSerializer.SerializeToElement(answer), Sources);
    }
}
