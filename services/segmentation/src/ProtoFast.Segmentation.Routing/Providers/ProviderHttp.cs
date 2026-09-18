namespace ProtoFast.Segmentation.Routing.Providers;

/// <summary>
/// Shared HTTP settings for provider SDKs. Timeouts belong to the router; these exist so the
/// SDKs cannot fire first.
/// </summary>
internal static class ProviderHttp
{
    /// <summary>
    /// Finite backstop for SDKs that put <c>Timeout</c> on the wire. Anthropic sends it as
    /// <c>X-Stainless-Timeout</c>, so <see cref="Timeout.InfiniteTimeSpan"/> would go out as
    /// <c>-0.001</c>. Sits one minute above the router's deadline so the router's CTS still wins.
    /// </summary>
    public static TimeSpan SdkBackstopFor(TimeSpan requestTimeout) =>
        requestTimeout + TimeSpan.FromMinutes(1);

    /// <summary>
    /// HttpClient for a provider SDK. Unbounded: HttpClient's default of 100s fires
    /// independently of the CancellationToken the router uses, which is how thinking
    /// models were dying at 100s with a minutes-long deadline sitting unused above it.
    /// </summary>
    public static HttpClient Create(HttpMessageHandler handler) =>
        new(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
}
