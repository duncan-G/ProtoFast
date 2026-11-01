namespace ProtoFast.Api.Services.Imaging;

/// <summary>The image provider could not be used; the message is worded for the operator.</summary>
public sealed class ImageException(string message, Exception? inner = null) : Exception(message, inner);
