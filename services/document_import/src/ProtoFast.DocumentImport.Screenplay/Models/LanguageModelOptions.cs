namespace ProtoFast.DocumentImport.Screenplay.Models;

public sealed class LanguageModelOptions
{
    /// <summary>Which provider agent executors run on; every model class maps within it.</summary>
    public string Default { get; set; } = LanguageModelProviders.DeepSeek;

    /// <summary>Takes a refused request on the same model class; skipped while it has no API key.</summary>
    public string? Fallback { get; set; }

    public int MaxOutputTokens { get; set; } = 128_000;

    public ModelRetryOptions Retry { get; set; } = new();

    /// <summary>Source text beyond this is cut before it reaches a model.</summary>
    public int MaxSourceChars { get; set; } = 120_000;

    /// <summary>Records prompts and replies on GenAI spans. They carry the manuscript, so dev only.</summary>
    public bool CaptureMessageContent { get; set; }

    public ProviderOptions Anthropic { get; set; } = new()
    {
        Large = "claude-opus-5-5",
        Medium = "claude-sonnet-5-5",
        Small = "claude-haiku-5-5",
        InputPricePerMillion = new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-opus-5-5"] = 4, ["claude-sonnet-5-5"] = 2, ["claude-haiku-5-5"] = 0.10m,
        },
        OutputPricePerMillion = new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-opus-5-5"] = 20, ["claude-sonnet-5-5"] = 10, ["claude-haiku-5-5"] = 0.50m,
        },
    };

    public ProviderOptions Gemini { get; set; } = new()
    {
        Large = "gemini-3.1-pro-preview",
        Medium = "gemini-3.8-flash",
        Small = "gemini-3.5-flash-lite",
        InputPricePerMillion = new(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini-3.1-pro-preview"] = 2, ["gemini-3.8-flash"] = 0.75m, ["gemini-3.5-flash-lite"] = 0.30m,
        },
        OutputPricePerMillion = new(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini-3.1-pro-preview"] = 12, ["gemini-3.8-flash"] = 3.75m, ["gemini-3.5-flash-lite"] = 2.50m,
        },
    };

    // Peak-hour prices; off-peak is half. Cache hits are automatic, with no write premium.
    public ProviderOptions DeepSeek { get; set; } = new()
    {
        BaseUrl = "https://api.deepseek.com/anthropic",
        Large = "deepseek-v4-pro",
        Medium = "deepseek-flash",
        Small = "deepseek-flash",
        InputPricePerMillion = new(StringComparer.OrdinalIgnoreCase)
        {
            ["deepseek-v4-pro"] = 1.32m, ["deepseek-flash"] = 0.30m,
        },
        OutputPricePerMillion = new(StringComparer.OrdinalIgnoreCase)
        {
            ["deepseek-v4-pro"] = 3.96m, ["deepseek-flash"] = 1.20m,
        },
        CacheWriteMultiplier = 1,
        CacheReadMultiplier = 1 / 30m,
    };

    public ProviderOptions Provider(string name) => name.ToLowerInvariant() switch
    {
        LanguageModelProviders.Anthropic => Anthropic,
        LanguageModelProviders.Gemini => Gemini,
        LanguageModelProviders.DeepSeek => DeepSeek,
        _ => throw new InvalidOperationException($"Unknown language model provider '{name}'."),
    };
}
