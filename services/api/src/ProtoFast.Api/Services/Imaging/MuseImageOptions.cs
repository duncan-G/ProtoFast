namespace ProtoFast.Api.Services.Imaging;

/// <summary>Bound from <c>Imaging:Muse</c>; the key comes from <c>Providers:Meta:ApiKey</c>.</summary>
public sealed class MuseImageOptions
{
    public string BaseUrl { get; set; } = "https://api.meta.ai/v1";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "muse-image-1.0";

    /// <summary>The Responses API takes only 1024x1024, 1536x1024 or 1024x1536.</summary>
    public string Size { get; set; } = "1536x1024";

    /// <summary>webp, png or jpeg.</summary>
    public string OutputFormat { get; set; } = "webp";

    public decimal PricePerImage { get; set; } = 0.01m;
}
