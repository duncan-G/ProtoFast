namespace ProtoFast.AppHost.LocalStack;

public static class LocalStackResourceBuilderExtensions
{
    /// <summary>The single gateway port LocalStack serves every emulated AWS API on.</summary>
    public const string GatewayEndpointName = "gateway";

    /// <summary>
    /// LocalStack's own port, pinned on the host as well as in the container. See
    /// <see cref="AddLocalStack"/> for why it is not left to Aspire to allocate.
    /// </summary>
    private const int GatewayPort = 4566;

    /// <summary>
    /// The hostname LocalStack has a publicly-trusted TLS certificate for. It is a real DNS record
    /// LocalStack owns that resolves to 127.0.0.1, which is what makes an ordinary browser or SDK
    /// trust it with no local CA to install — <c>localhost</c> itself has no such certificate.
    /// </summary>
    private const string GatewayHost = "localhost.localstack.cloud";

    /// <summary><see cref="GatewayHost"/> and <see cref="GatewayPort"/> as LocalStack's own
    /// <c>LOCALSTACK_HOST</c> variable expects them. Kept out of the interpolated string inline in
    /// <see cref="AddLocalStack"/> because Aspire's <c>WithEnvironment</c> intercepts an inline
    /// interpolated string with its own handler, which only accepts Aspire value providers — not a
    /// plain <see langword="int"/> like <see cref="GatewayPort"/> — as a hole.</summary>
    private static readonly string GatewayHostAndPort = $"{GatewayHost}:{GatewayPort}";

    /// <summary>
    /// The gateway's HTTPS address, fixed because <see cref="GatewayPort"/> is pinned and
    /// unproxied in every mode. Every consumer of the gateway URL uses this instead of
    /// <c>GetEndpoint</c>: the Envoy-served client pages are HTTPS (see
    /// <c>EnvoyProxyResourceBuilderExtensions.GetClientOrigins</c>), and a browser on an HTTPS page
    /// refuses to <c>PUT</c> a presigned URL that comes back <c>http://</c> — the request never
    /// leaves the tab, and it is reported as a mixed-content error rather than anything CORS- or
    /// LocalStack-shaped.
    /// </summary>
    public static string GatewayUrl => $"https://{GatewayHostAndPort}";

    /// <summary>
    /// Adds LocalStack with S3 and SQS, and creates the segmentation bucket and the three queues
    /// on container start (plan §22.1).
    ///
    /// <para>The init script is what makes a fresh clone work: without it, <c>aspire run</c> would
    /// come up with a worker polling a queue that does not exist, and every developer would have
    /// to run the same four <c>awslocal</c> commands by hand before anything worked.</para>
    /// </summary>
    public static IResourceBuilder<ContainerResource> AddLocalStack(
        this IDistributedApplicationBuilder builder,
        string name,
        string bucketName,
        string region) =>
        builder
            .AddContainer(name, "localstack/localstack", "4")
            // Unproxied and pinned, unlike most endpoints here. A presigned URL embeds the host and
            // port it was signed against and the browser is what dials it, so the gateway's address
            // leaves the process and ends up in a page. Behind Aspire's proxy that address is a
            // fresh random port every `aspire run`, which makes any URL a tab is still holding
            // point at nothing after a restart — and the failed OPTIONS that follows is reported by
            // the browser as a CORS error, sending you to look at the bucket's CORS rules for a
            // problem that is really a dead port. Pinning to LocalStack's own 4566 also makes the
            // host's `aws --endpoint-url http://localhost:4566` agree with the container's
            // `awslocal`, so a URL copied between them still verifies.
            .WithHttpEndpoint(
                port: GatewayPort, targetPort: GatewayPort, name: GatewayEndpointName, isProxied: false)
            .WithEnvironment("SERVICES", "s3,sqs")
            .WithEnvironment("DEBUG", "0")
            // The gateway serves both plain HTTP and TLS on the same port (LocalStack sniffs the
            // first bytes of the connection), so this widens what it accepts rather than narrowing
            // it — the health check below and any host-side `awslocal`/`aws` call over plain HTTP
            // still work unchanged. USE_SSL and LOCALSTACK_HOST only govern the scheme and host
            // LocalStack itself stamps into any URL it generates, kept in step with GatewayUrl so
            // nothing it echoes back disagrees with what the clients were actually given.
            .WithEnvironment("USE_SSL", "1")
            .WithEnvironment("LOCALSTACK_HOST", GatewayHostAndPort)
            .WithEnvironment("SEGMENTATION_BUCKET", bucketName)
            // The region the init script creates in. LocalStack keeps queues per region, so this
            // has to be the same value the services sign their calls with (Seg_Storage__Region) —
            // a queue created in one region is simply absent in another.
            .WithEnvironment("AWS_DEFAULT_REGION", region)
            // ready.d runs once the emulated services are actually accepting calls, which
            // init-ready scripts elsewhere in the tree are not; anything earlier races the
            // services it is trying to configure.
            .WithBindMount(
                "../scripts/localstack-init.sh",
                "/etc/localstack/init/ready.d/segmentation.sh",
                isReadOnly: true)
            .WithHttpHealthCheck("/_localstack/health", statusCode: 200, endpointName: GatewayEndpointName);

    /// <summary>
    /// Tells LocalStack which browser origins the presigned PUT arrives from.
    ///
    /// <para>Two things read this, and both are needed:</para>
    /// <list type="bullet">
    /// <item><c>SEGMENTATION_CORS_ORIGINS</c> is what scripts/localstack-init.sh puts in the
    /// bucket's own CORS rules. Those rules are authoritative: with a CORS configuration present,
    /// LocalStack answers the preflight exactly as real S3 does, and an origin or method the rules
    /// do not list gets a <c>403</c> on the <c>OPTIONS</c> — which the browser reports as a CORS
    /// error even though the <c>PUT</c> itself would have succeeded. Sourcing the list here keeps
    /// it from drifting from the Envoy listener ports that produce it.</item>
    /// <item><c>EXTRA_CORS_ALLOWED_ORIGINS</c> is the fallback LocalStack applies when the bucket
    /// has no CORS configuration at all — the state a fresh stack is in if the init script failed
    /// partway. Without it that case fails the preflight for every origin and looks identical to a
    /// misconfigured rule; with it the upload works and the missing rules stay a startup-log
    /// problem. It does not widen or duplicate the bucket rules when they exist.</item>
    /// </list>
    /// </summary>
    public static IResourceBuilder<ContainerResource> WithClientOrigins(
        this IResourceBuilder<ContainerResource> localstack,
        IReadOnlyList<string> origins)
    {
        if (origins.Count == 0)
        {
            return localstack;
        }

        var list = string.Join(',', origins);

        return localstack
            .WithEnvironment("SEGMENTATION_CORS_ORIGINS", list)
            .WithEnvironment("EXTRA_CORS_ALLOWED_ORIGINS", list);
    }

    /// <summary>
    /// The queue URL a client should use, built from <see cref="GatewayUrl"/> so it agrees with
    /// every other consumer of the gateway rather than being derived separately.
    /// </summary>
    public static string QueueUrl(
        this IResourceBuilder<ContainerResource> localstack, string queueName) =>
        $"{GatewayUrl}/000000000000/{queueName}";
}
