namespace ProtoFast.DocumentImport.Core;

public static class DocumentImportQueues
{
    /// <summary>The keyed <c>IMessageQueue</c> that carries <see cref="DocumentImportRequested"/>.</summary>
    public const string ImportQueueKey = "document-import";

    /// <summary>The keyed <c>IMessageQueue</c> of uploads waiting for the conversion worker.</summary>
    public const string ConversionQueueKey = "document-conversion";
}
