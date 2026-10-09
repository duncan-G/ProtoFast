namespace ProtoFast.Api.Services.Imaging;

/// <param name="Caption">Sent just before the image: what it is, so the model knows what to take from it.</param>
/// <param name="Uri">Where the image is stored, so a span can name it when it is too big to carry.</param>
public sealed record ImageInput(byte[] Bytes, string ContentType, string Caption, string Uri = "");
