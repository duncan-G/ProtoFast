namespace ProtoFast.DocumentImport.Screenplay.Models;

public static class LanguageModelProviders
{
    public const string Anthropic = "anthropic";
    public const string Gemini = "gemini";
    public const string DeepSeek = "deepseek";

    /// <summary>The OpenTelemetry <c>gen_ai.provider.name</c> and API host for a configured provider.</summary>
    public static (string ProviderName, string ServerAddress) Telemetry(string name) => name.ToLowerInvariant() switch
    {
        Anthropic => ("anthropic", "api.anthropic.com"),
        Gemini => ("gcp.gemini", "generativelanguage.googleapis.com"),
        DeepSeek => ("deepseek", "api.deepseek.com"),
        _ => throw new InvalidOperationException($"Unknown language model provider '{name}'."),
    };
}
