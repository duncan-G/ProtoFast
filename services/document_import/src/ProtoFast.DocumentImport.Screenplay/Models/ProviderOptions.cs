namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <summary>Bound from <c>Providers:{name}</c>; the API key arrives from Secrets Manager under the same path.</summary>
public sealed class ProviderOptions
{
    public string? ApiKey { get; set; }

    /// <summary>Overrides the SDK's endpoint, for providers that serve another's API.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Model ids per engine model class.</summary>
    public string Large { get; set; } = "";

    public string Medium { get; set; } = "";

    public string Small { get; set; } = "";

    /// <summary>USD per million tokens, keyed by model id; missing models cost 0.</summary>
    public Dictionary<string, decimal> InputPricePerMillion { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> OutputPricePerMillion { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prompt-cache writes and reads as multiples of the input price.</summary>
    public decimal CacheWriteMultiplier { get; set; } = 1.25m;

    public decimal CacheReadMultiplier { get; set; } = 0.1m;

    /// <summary>Output caps for models below <see cref="LanguageModelOptions.MaxOutputTokens"/>, keyed by model id.</summary>
    public Dictionary<string, int> MaxOutputTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int MaxOutputTokensOf(string modelId, int limit) =>
        MaxOutputTokens.TryGetValue(modelId, out var cap) ? Math.Min(cap, limit) : limit;

    public decimal PriceOf(string modelId, long inputTokens, long outputTokens)
    {
        InputPricePerMillion.TryGetValue(modelId, out var input);
        OutputPricePerMillion.TryGetValue(modelId, out var output);
        return (input * inputTokens + output * outputTokens) / 1_000_000m;
    }
}
