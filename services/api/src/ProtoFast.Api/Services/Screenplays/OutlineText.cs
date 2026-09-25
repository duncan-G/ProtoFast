namespace ProtoFast.Api.Services.Screenplays;

public static class OutlineText
{
    public const int MaxLength = 255;
    public const string UntitledScene = "Untitled scene";
    public const string FirstContainer = "Act I";

    public static string StoryTitle(string? title)
    {
        var trimmed = title?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw StoryErrors.Invalid("A story needs a title.");
        }

        return trimmed.Length <= MaxLength
            ? trimmed
            : throw StoryErrors.Invalid($"Titles are limited to {MaxLength} characters.");
    }

    public static string SceneTitle(string? title)
    {
        var trimmed = title?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return UntitledScene;
        }

        return trimmed.Length <= MaxLength
            ? trimmed
            : throw StoryErrors.Invalid($"Titles are limited to {MaxLength} characters.");
    }

    public static string ContainerLabel(string? label)
    {
        var trimmed = label?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw StoryErrors.Invalid("A container needs a label.");
        }

        return trimmed.Length <= MaxLength
            ? trimmed
            : throw StoryErrors.Invalid($"Labels are limited to {MaxLength} characters.");
    }
}
