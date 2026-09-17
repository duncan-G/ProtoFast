using System.Text.Json;
using Microsoft.Extensions.AI;
using ProtoFast.Segmentation.Routing.Providers;

namespace ProtoFast.Segmentation.ContractTests;

/// <summary>
/// Writes a recording to a temporary directory and hands back a <see cref="ReplayChatClient"/>
/// pointed at it.
///
/// <para>Recordings are keyed by a hash of the prompt, which means a test that changes its prompt
/// misses its recording loudly rather than replaying an answer to a different question. Building
/// them here rather than committing fixtures keeps the key and the prompt in the same file, so
/// the two cannot drift.</para>
/// </summary>
internal sealed class RecordingScope : IDisposable
{
    private readonly string _directory;

    public RecordingScope()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pf-seg-recordings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public string Directory1 => _directory;

    public RecordingScope Add(string prompt, ReplayChatClient.Recording recording)
    {
        var key = ReplayChatClient.KeyFor([new ChatMessage(ChatRole.User, prompt)]);

        File.WriteAllText(
            Path.Combine(_directory, $"{key}.json"),
            JsonSerializer.Serialize(recording, Options));

        return this;
    }

    public ReplayChatClient Client(string model = "test/model") => new(_directory, model);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
