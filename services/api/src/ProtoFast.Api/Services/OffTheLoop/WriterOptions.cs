namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>Bound from <c>Writer</c>; DeepSeek's key and endpoint come from <see cref="ModelProviders"/>.</summary>
public sealed class WriterOptions
{
    public string Model { get; set; } = "deepseek-v4-pro";

    public int MaxOutputTokens { get; set; } = 16_000;
}
