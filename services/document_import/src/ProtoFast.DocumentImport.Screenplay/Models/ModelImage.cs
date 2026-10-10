namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <summary>A picture shown to a model in a user turn.</summary>
/// <param name="Caption">Sent just before the picture: what it is, so the model knows what to take from it.</param>
/// <param name="Uri">Where it is stored, which a span records in place of the bytes.</param>
public sealed record ModelImage(byte[] Bytes, string ContentType, string Caption, string Uri = "");
