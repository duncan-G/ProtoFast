using System.Security.Cryptography;
using System.Text;

namespace ProtoFast.Api.Services.Imaging;

public static class ImageRequests
{
    /// <summary>
    /// What a picture is of: the media cache key, and what makes it stale. The references a frame is
    /// composed from are left out, since the orchestrator picks them afresh each time.
    /// </summary>
    public static string Hash(IImageGenerator generator, string size, string prompt) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', generator.Provider, generator.Model, size, prompt))));

    public static string Extension(string contentType) => contentType.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "image/webp" => "webp",
        "image/png" => "png",
        "image/jpeg" or "image/jpg" => "jpg",
        _ => "bin",
    };
}
