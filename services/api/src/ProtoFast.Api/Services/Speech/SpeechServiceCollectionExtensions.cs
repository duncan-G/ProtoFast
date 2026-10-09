namespace ProtoFast.Api.Services.Speech;

public static class SpeechServiceCollectionExtensions
{
    /// <summary>The speech provider <c>Speech:Provider</c> names, with its own HTTP client.</summary>
    public static IServiceCollection AddSpeechSynthesis(this IServiceCollection services, Action<SpeechOptions> configure)
    {
        var options = new SpeechOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient(OpenAiSpeechSynthesizer.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient(ElevenLabsSpeechSynthesizer.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<ISpeechSynthesizer>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return options.Provider.ToLowerInvariant() switch
            {
                SpeechProviders.OpenAi => new OpenAiSpeechSynthesizer(options.OpenAi, factory.CreateClient(OpenAiSpeechSynthesizer.HttpClientName)),
                SpeechProviders.ElevenLabs => new ElevenLabsSpeechSynthesizer(options.ElevenLabs, factory.CreateClient(ElevenLabsSpeechSynthesizer.HttpClientName)),
                _ => throw new InvalidOperationException($"Unknown speech provider '{options.Provider}'."),
            };
        });
        services.AddScoped<CueAudioGenerator>();
        services.AddScoped<CueAudioMessages>();
        return services;
    }
}
