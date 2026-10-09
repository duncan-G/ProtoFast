namespace ProtoFast.Api.Services.Imaging;

/// <param name="Size">One of the provider's sizes, e.g. <c>1536x1024</c>.</param>
/// <param name="Preamble">Sent ahead of the references; empty when there are none.</param>
/// <param name="References">Each sent as its caption, then the image.</param>
/// <param name="Prompt">Sent last: the picture to make.</param>
public sealed record ImageRequest(string Size, string Preamble, IReadOnlyList<ImageInput> References, string Prompt)
{
    /// <summary>Every text part in the order it was sent, for the record.</summary>
    public string Transcript() => string.Join("\n\n",
        new[] { Preamble }.Concat(References.Select(r => r.Caption)).Append(Prompt).Where(t => t.Length > 0));
}
