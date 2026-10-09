namespace ProtoFast.Api.Services.Speech;

public static class SpeechServiceCollectionExtensions
{
    /// <summary>ElevenLabs as the speech provider, with its own HTTP client.</summary>
    public static IServiceCollection AddSpeechSynthesis(this IServiceCollection services, Action<SpeechOptions> configure)
    {
        var options = new SpeechOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient(ElevenLabsSpeechSynthesizer.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<ISpeechSynthesizer>(sp => new TracedSpeechSynthesizer(
            new ElevenLabsSpeechSynthesizer(
                options.ElevenLabs, sp.GetRequiredService<IHttpClientFactory>().CreateClient(ElevenLabsSpeechSynthesizer.HttpClientName)),
            new Uri(options.ElevenLabs.BaseUrl).Host,
            options.CaptureMessageContent));
        services.AddScoped<CueAudioGenerator>();
        services.AddScoped<CueAudioMessages>();
        return services;
    }
}
