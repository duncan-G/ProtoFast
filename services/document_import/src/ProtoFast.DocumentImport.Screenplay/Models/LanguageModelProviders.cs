namespace ProtoFast.DocumentImport.Screenplay.Models;

public static class LanguageModelProviders
{
    public const string Anthropic = "anthropic";
    public const string Gemini = "gemini";

    /// <summary>The OpenTelemetry <c>gen_ai.provider.name</c> and API host for a configured provider.</summary>
    public static (string ProviderName, string ServerAddress) Telemetry(string name) => name.ToLowerInvariant() switch
    {
        Anthropic => ("anthropic", "api.anthropic.com"),
        Gemini => ("gcp.gemini", "generativelanguage.googleapis.com"),
        _ => throw new InvalidOperationException($"Unknown language model provider '{name}'."),
    };
}
