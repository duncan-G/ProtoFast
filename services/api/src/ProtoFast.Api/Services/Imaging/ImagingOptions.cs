namespace ProtoFast.Api.Services.Imaging;

/// <summary>Bound from <c>Imaging</c>; the Muse key comes from <c>Providers:Meta:ApiKey</c>.</summary>
public sealed class ImagingOptions
{
    public MuseImageOptions Muse { get; set; } = new();

    /// <summary>Opens every prompt, so every picture in a story looks like the same film.</summary>
    public string Style { get; set; } =
        "Cinematic still from a live-action feature film: naturalistic light and colour, photographic detail, 35mm lens.";

    /// <summary>Muse's Responses API takes only its own sizes: 1024x1024, 1536x1024, 1024x1536.</summary>
    public string FrameSize { get; set; } = "1536x1024";

    public string CharacterSize { get; set; } = "1536x1024";

    public string LocationSize { get; set; } = "1536x1024";

    public string PropSize { get; set; } = "1024x1024";

    /// <summary>Records captions, prompts and reasoning on the image spans; copied from <c>Providers:CaptureMessageContent</c>.</summary>
    public bool CaptureMessageContent { get; set; }

    /// <summary>The most existing pictures a frame is offered to compose from, most useful first.</summary>
    public int MaxReferences { get; set; } = 8;
}
