using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtoFast.Api.Services.Screenplays.Files;

public static class TheplotJson
{
    public const string Extension = ".theplot";

    /// <summary>gRPC's default receive limit; <see cref="StoryFileService"/> allows the framing on top.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    private const int FileNameMaxLength = 120;

    private const string NotAFile = "This isn’t a .theplot file.";

    // Unknown, duplicate, missing or null members and numeric enums are refused, never guessed at.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static byte[] Write(TheplotFile file) => JsonSerializer.SerializeToUtf8Bytes(file, Options);

    /// <summary>Checks the format and version before anything else, so a newer file says so.</summary>
    public static TheplotFile Read(ReadOnlyMemory<byte> content)
    {
        if (content.Length > MaxBytes)
        {
            throw StoryErrors.Invalid($"A .theplot file can be up to {MaxBytes / (1024 * 1024)} MB.");
        }

        using var document = Parse(content);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("format", out var format)
            || format.ValueKind != JsonValueKind.String
            || format.GetString() != TheplotFile.FormatName
            || !root.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var number)
            || number < 1)
        {
            throw StoryErrors.Invalid(NotAFile);
        }

        if (number > TheplotFile.CurrentVersion)
        {
            throw StoryErrors.Invalid("This file was saved by a newer version of ThePlot, so it can’t be imported yet.");
        }

        try
        {
            return root.Deserialize<TheplotFile>(Options) ?? throw StoryErrors.Invalid(NotAFile);
        }
        catch (JsonException exception)
        {
            throw StoryErrors.Invalid($"This .theplot file has something ThePlot can’t read at {exception.Path ?? "$"}.");
        }
    }

    /// <summary>The title without the characters file systems reserve, ending in <see cref="Extension"/>.</summary>
    public static string FileName(string title)
    {
        var safe = title.Select(c => char.IsControl(c) || "\\/:*?\"<>|".Contains(c) ? ' ' : c).ToArray();
        var name = string.Join(' ', new string(safe).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (name.Length > FileNameMaxLength)
        {
            name = name[..(char.IsHighSurrogate(name[FileNameMaxLength - 1]) ? FileNameMaxLength - 1 : FileNameMaxLength)]
                .TrimEnd('.', ' ');
        }

        return (name.Length == 0 ? "Story" : name) + Extension;
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> content)
    {
        try
        {
            return JsonDocument.Parse(content, new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException)
        {
            throw StoryErrors.Invalid(NotAFile);
        }
    }
}
