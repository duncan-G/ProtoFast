namespace ProtoFast.Api.Services.OffTheLoop;

public static class LoopIds
{
    /// <summary>An unreadable id is reported like any id the caller does not own.</summary>
    public static Guid Existing(string? id, string notFound) =>
        Guid.TryParse(id, out var parsed) ? parsed : throw LoopErrors.NotFound(notFound);
}
