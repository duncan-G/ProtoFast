namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>What a resume or CV may be sent as, told by the file's extension.</summary>
public static class DocumentFormats
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly DocumentFormat[] All =
    [
        new(".pdf", "application/pdf"),
        new(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        new(".doc", "application/msword"),
        new(".odt", "application/vnd.oasis.opendocument.text"),
        new(".rtf", "application/rtf"),
        new(".txt", "text/plain"),
        new(".md", "text/markdown"),
    ];

    public static DocumentFormat Of(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return All.FirstOrDefault(f => f.Extension == extension)
               ?? throw LoopErrors.Invalid("Send a PDF, Word, OpenDocument, RTF, text or Markdown file.");
    }
}
