namespace ProtoFast.Api.Services.WorldModels;

public static class WorldModelMessages
{
    public static long Micros(decimal usd) => (long)Math.Round(usd * 1_000_000m);

    /// <summary>Stored as UTC without a kind, as the date-stamp interceptor writes them.</summary>
    public static long Millis(DateTime? at) =>
        at is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds() : 0;
}
