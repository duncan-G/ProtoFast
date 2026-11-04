namespace ProtoFast.Api.Services.OffTheLoop;

public interface IResearchModel
{
    /// <summary>Each search and page read as it starts, then the findings, last.</summary>
    IAsyncEnumerable<ResearchUpdate> ResearchAsync(ResearchBrief brief, CancellationToken cancellationToken);
}
