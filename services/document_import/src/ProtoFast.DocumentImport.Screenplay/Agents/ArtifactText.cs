using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

public static class ArtifactText
{
    public static async Task<string> ReadAsync(IArtifactStore artifacts, ArtifactRef reference, CancellationToken ct)
    {
        await using var stream = await artifacts.GetAsync(reference, ct);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    public static async Task<string> ReadAsync(Stream stream, CancellationToken ct)
    {
        await using (stream)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
    }
}
