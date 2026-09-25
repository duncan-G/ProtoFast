namespace ProtoFast.Api.Services.Screenplays;

public static class StoryIds
{
    /// <summary>An unreadable id is reported like any id the caller does not own.</summary>
    public static Guid Existing(string? id, string notFound) =>
        Guid.TryParse(id, out var parsed) ? parsed : throw StoryErrors.NotFound(notFound);

    public static Guid New(string? id, string invalid) =>
        Guid.TryParse(id, out var parsed) && parsed != Guid.Empty ? parsed : throw StoryErrors.Invalid(invalid);
}
