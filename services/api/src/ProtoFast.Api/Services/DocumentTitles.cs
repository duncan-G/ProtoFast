using ProtoFast.DocumentImport.Core;

namespace ProtoFast.Api.Services;

public static class DocumentTitles
{
    private const int MaxLength = 255;

    /// <summary>
    /// The title a freshly imported document gets: the file name without its extension, with
    /// dashes and underscores read as spaces and each word capitalised — <c>the-quiet_year.docx</c>
    /// becomes <c>The Quiet Year</c>. Only the last path segment is considered, and a name that is
    /// nothing but an extension falls back to the file name itself.
    /// </summary>
    public static string FromFileName(string fileName)
    {
        var name = fileName.AsSpan().Trim();
        var lastSeparator = name.LastIndexOfAny('/', '\\');
        if (lastSeparator >= 0)
        {
            name = name[(lastSeparator + 1)..];
        }

        var extension = SourceFormats.ExtensionOf(name.ToString());
        if (extension is not null)
        {
            name = name[..^extension.Length];
        }

        var words = name.ToString()
            .Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);

        var title = string.Join(' ', words);
        if (title.Length == 0)
        {
            title = fileName.Trim();
        }

        return title.Length > MaxLength ? title[..MaxLength] : title;
    }
}
