using System.Text.Json;
using ProtoFast.Segmentation.Core.Ingest;

namespace ProtoFast.Segmentation.Cli;

/// <summary>Reading a document and its optional layout sibling off disk — the CLI's only I/O.</summary>
internal static class DocumentFiles
{
    public static async Task<(string Markdown, LayoutDocument? Layout)> ReadAsync(string path, string? layoutPath)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"'{path}' does not exist.", path);
        }

        var markdown = await File.ReadAllTextAsync(path);

        // Fall back to the conventional sibling name, so --layout is only needed when it differs.
        layoutPath ??= Path.ChangeExtension(path, ".layout.json");

        if (!File.Exists(layoutPath))
        {
            return (markdown, null);
        }

        var layout = JsonSerializer.Deserialize<LayoutDocument>(
            await File.ReadAllTextAsync(layoutPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return (markdown, layout);
    }
}
