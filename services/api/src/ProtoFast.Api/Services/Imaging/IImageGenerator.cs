namespace ProtoFast.Api.Services.Imaging;

/// <summary>Renders a picture from a prompt, composed from reference images when there are any.</summary>
public interface IImageGenerator
{
    string Provider { get; }

    string Model { get; }

    Task<GeneratedImage> GenerateAsync(ImageRequest request, CancellationToken ct);
}
