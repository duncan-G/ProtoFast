namespace ProtoFast.Api.Services.Directing;

/// <summary>The model answered, but not in a shape the director could use.</summary>
public sealed class ModelOutputException(string message) : Exception(message);
