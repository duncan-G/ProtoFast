using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.Api.Services.Directing;

/// <summary>Bound from <c>Director</c>.</summary>
public sealed class DirectorOptions
{
    /// <summary>One of <see cref="ModelClasses"/>, on the default provider; the agent's class, so direction reads as well as import.</summary>
    public string ModelClass { get; set; } = ModelClasses.Large;

    /// <summary>How many lines about an entity a sheet is written from, earliest first.</summary>
    public int MaxExcerpts { get; set; } = 60;
}
