namespace ProtoFast.Api.Services.Imaging;

public interface IImageGenerator
{
    /// <param name="references">Pictures to draw from, each sent after its caption and before the prompt.</param>
    Task<GeneratedImage> GenerateAsync(string prompt, IReadOnlyList<ImageInput> references, CancellationToken ct);
}
