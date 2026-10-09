using System.Security.Cryptography;
using System.Text;
using ProtoFast.Api.Services.Imaging;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Draws every picture as a fresh run of bytes, and remembers what it was asked.</summary>
public sealed class ScriptedImageGenerator : IImageGenerator
{
    private int _drawn;

    public List<ImageRequest> Requests { get; } = [];

    public string Provider => "scripted";

    public string Model => "scripted-1";

    public Task<GeneratedImage> GenerateAsync(ImageRequest request, CancellationToken ct)
    {
        lock (Requests)
        {
            Requests.Add(request);
            _drawn++;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{_drawn}:{request.Prompt}"));
        return Task.FromResult(new GeneratedImage(
            bytes, "image/webp", 10_000, $"Drew it from {request.References.Count} references.",
            new ImageUsage($"resp_{_drawn}", "scripted-1-0901", "completed", 1200, 900, 80)));
    }
}
