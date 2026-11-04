namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>Bound from <c>Providers:{name}</c>; the key arrives in the secret as <c>Api_Providers__{name}__ApiKey</c>.</summary>
public sealed class ModelProvider
{
    /// <summary>Without one, what runs on the provider answers unavailable.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "";

    /// <summary>The model id of its medium class.</summary>
    public string Medium { get; set; } = "";

    /// <summary>USD per million tokens, keyed by model id; missing models cost 0.</summary>
    public Dictionary<string, decimal> InputPricePerMillion { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> OutputPricePerMillion { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cached input as a multiple of the input price.</summary>
    public decimal CacheReadMultiplier { get; set; } = 0.1m;

    public decimal PriceOf(string model, ModelUsage usage)
    {
        InputPricePerMillion.TryGetValue(model, out var input);
        OutputPricePerMillion.TryGetValue(model, out var output);
        var billedInput = usage.InputTokens - usage.CachedInputTokens + usage.CachedInputTokens * CacheReadMultiplier;
        return (input * billedInput + output * usage.OutputTokens) / 1_000_000m;
    }
}
