namespace ProtoFast.DocumentImport.Screenplay.Classification;

/// <summary>The classifier model answered, but not with a family the engine can use.</summary>
public sealed class DocumentClassificationException(string message) : Exception(message);
