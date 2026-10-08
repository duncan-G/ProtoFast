namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <summary>
/// How long one request waits out a provider outage before the model is given up as unavailable.
/// The provider SDK's own short retries cover blips; this covers minutes.
/// </summary>
public sealed class ModelRetryOptions
{
    /// <summary>The first wait; each later one doubles it up to <see cref="MaxDelay"/>.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Total waiting on one request before it fails as unavailable. Zero disables retries.</summary>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromMinutes(10);
}
