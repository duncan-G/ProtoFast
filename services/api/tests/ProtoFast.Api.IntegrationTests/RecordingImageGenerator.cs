using System.Collections.Concurrent;
using ProtoFast.Api.Services.Imaging;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Draws nothing; keeps each prompt it was asked, with the references it was handed.</summary>
public sealed class RecordingImageGenerator : IImageGenerator
{
    public static readonly byte[] Picture = [0x52, 0x49, 0x46, 0x46];

    public ConcurrentQueue<(string Prompt, IReadOnlyList<ImageInput> References)> Calls { get; } = new();

    public Task<GeneratedImage> GenerateAsync(string prompt, IReadOnlyList<ImageInput> references, CancellationToken ct)
    {
        Calls.Enqueue((prompt, references));
        return Task.FromResult(new GeneratedImage(Picture, "image/webp", 10_000, "Kept the counter in view.",
            new ImageUsage("resp_1", "muse-image-1.0-20261001", "completed", 1200, 1000, 1)));
    }
}
