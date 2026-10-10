using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.Api.Services.Directing;

/// <summary>Bound from <c>Director</c>.</summary>
public sealed class DirectorOptions
{
    /// <summary>One of <see cref="ModelClasses"/>, on the default provider.</summary>
    public string ModelClass { get; set; } = ModelClasses.Medium;

    /// <summary>
    /// The provider that suggests a single frame, verifies frames and fixes them: it is shown the
    /// pictures already made, which the default provider may not take.
    /// </summary>
    public string VisionProvider { get; set; } = LanguageModelProviders.Anthropic;

    /// <summary>One of <see cref="ModelClasses"/>, on <see cref="VisionProvider"/>.</summary>
    public string VisionModelClass { get; set; } = ModelClasses.Medium;

    /// <summary>Check directed frames for consistency and positioning, and have the model fix what is found once.</summary>
    public bool Verify { get; set; } = true;

    /// <summary>How many lines about an entity a sheet is written from, earliest first.</summary>
    public int MaxExcerpts { get; set; } = 60;
}
