namespace ProtoFast.Api.Services;

/// <summary>
/// GenAI message parts for images and audio. Media goes inline as a base64 <c>blob</c> part, which
/// the Aspire dashboard's GenAI visualizer shows or plays, until the budget runs out; after that a
/// part names where the media is stored instead.
/// </summary>
/// <param name="budgetBytes">
/// Raw bytes this span may carry; base64 adds a third, and every OTLP hop here is gRPC, which
/// refuses an export over 4 MiB.
/// </param>
public sealed class GenAiMedia(int budgetBytes)
{
    private int _left = budgetBytes;

    /// <param name="modality"><c>image</c>, <c>audio</c> or <c>video</c>.</param>
    /// <param name="uri">Where the media is stored, for when it doesn't fit; empty when it isn't stored yet.</param>
    public object Part(string modality, byte[] bytes, string mimeType, string uri = "")
    {
        if (bytes.Length <= _left)
        {
            _left -= bytes.Length;
            return new { type = "blob", modality, mime_type = mimeType, content = Convert.ToBase64String(bytes) };
        }

        return uri.Length > 0
            ? new { type = "uri", modality, mime_type = mimeType, uri }
            : new { type = modality, mime_type = mimeType, size = bytes.Length };
    }
}
