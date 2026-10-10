namespace ProtoFast.Api.Services.Imaging;

public static class ImagingServiceCollectionExtensions
{
    /// <summary>Muse as the image provider, with its own HTTP client.</summary>
    public static IServiceCollection AddImageGeneration(this IServiceCollection services, Action<ImagingOptions> configure)
    {
        var options = new ImagingOptions();
        configure(options);
        services.AddSingleton(options);
        // Muse refines over several passes by default; a frame with references takes a while.
        services.AddHttpClient(MuseImageGenerator.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<IImageGenerator>(sp => new TracedImageGenerator(
            new MuseImageGenerator(options.Muse, sp.GetRequiredService<IHttpClientFactory>().CreateClient(MuseImageGenerator.HttpClientName)),
            new Uri(options.Muse.BaseUrl).Host,
            options.CaptureMessageContent));
        services.AddSingleton<ImageStaleness>();
        services.AddSingleton<ReferenceCollector>();
        services.AddScoped<ImageAssets>();
        services.AddScoped<ImageMessages>();
        services.AddScoped<ImageRefiner>();
        services.AddScoped<FrameImageGenerator>();
        services.AddScoped<LibraryImageGenerator>();
        services.AddScoped<LookImageGenerator>();
        return services;
    }
}
