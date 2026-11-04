namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>A file the model reads itself, such as a PDF.</summary>
public sealed record ResearchFile(byte[] Data, string MediaType);
