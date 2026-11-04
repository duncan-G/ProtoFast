namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>Bound from <c>Providers</c>, named as document-import names them.</summary>
public sealed class ModelProviders
{
    public ModelRetryOptions Retry { get; set; } = new();

    /// <summary>Records prompts and replies on GenAI spans. They carry the user's notes and job search, so dev only.</summary>
    public bool CaptureMessageContent { get; set; }

    /// <summary>Through the Anthropic Messages API it serves, priced at peak hours; off-peak is half.</summary>
    public ModelProvider DeepSeek { get; set; } = new()
    {
        BaseUrl = "https://api.deepseek.com/anthropic",
        InputPricePerMillion = new(StringComparer.OrdinalIgnoreCase) { ["deepseek-v4-pro"] = 1.32m, ["deepseek-flash"] = 0.30m },
        OutputPricePerMillion = new(StringComparer.OrdinalIgnoreCase) { ["deepseek-v4-pro"] = 3.96m, ["deepseek-flash"] = 1.20m },
        CacheReadMultiplier = 1 / 30m,
    };

    /// <summary>Priced by tokens alone; grounded searches bill on top, per query.</summary>
    public ModelProvider Gemini { get; set; } = new()
    {
        BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
        Medium = "gemini-3.8-flash",
        InputPricePerMillion = new(StringComparer.OrdinalIgnoreCase) { ["gemini-3.8-flash"] = 0.75m },
        OutputPricePerMillion = new(StringComparer.OrdinalIgnoreCase) { ["gemini-3.8-flash"] = 3.75m },
    };
}
