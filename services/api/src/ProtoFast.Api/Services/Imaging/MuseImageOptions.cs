namespace ProtoFast.Api.Services.Imaging;

public sealed class MuseImageOptions
{
    public string BaseUrl { get; set; } = "https://api.meta.ai/v1";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "muse-image-1.0";

    /// <summary>webp, png or jpeg.</summary>
    public string OutputFormat { get; set; } = "webp";

    /// <summary>high (refines over several passes) or low (one pass); empty leaves Muse's default, high.</summary>
    public string ReasoningStrength { get; set; } = "";

    /// <summary>Lets Muse look up real places and things; off, since a story's world is its own.</summary>
    public bool EnableImageSearch { get; set; }

    public decimal PricePerImage { get; set; } = 0.01m;

    /// <summary>Wait before the first retry of a timed-out or failed call; doubles for each of up to three retries.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
