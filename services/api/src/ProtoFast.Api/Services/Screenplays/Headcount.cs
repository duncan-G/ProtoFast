namespace ProtoFast.Api.Services.Screenplays;

/// <summary>How many people an extra is; a picture draws exactly that many.</summary>
public static class Headcount
{
    public const int Max = 500;

    public static int? Check(bool has, int count) =>
        !has ? null
        : count is >= 1 and <= Max ? count
        : throw StoryErrors.Invalid($"A headcount is between 1 and {Max}.");

    /// <summary>A model's number out of range is no number at all.</summary>
    public static int? FromModel(int? count) => count is >= 1 and <= Max ? count : null;

    public static string Words(int count) => count == 1 ? "one person" : $"{count} people";
}
