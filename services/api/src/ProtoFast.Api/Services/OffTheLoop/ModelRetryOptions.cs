namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>
/// How long a call waits out a provider outage before it fails. Someone is watching it, so the window
/// is short; the Anthropic SDK's own retries cover blips under it.
/// </summary>
public sealed class ModelRetryOptions
{
    /// <summary>The first wait; each later one doubles it up to <see cref="MaxDelay"/>.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Total waiting on one call before it fails. Zero disables retries.</summary>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromSeconds(30);
}
