using Microsoft.Extensions.Configuration;

namespace ProtoFast.AppHost;

/// <summary>
/// LocalExtensions add what a resource needs under `aspire run` only. They add nothing to the
/// spec: in an account ProtoStrap gives each replica its credentials and telemetry endpoint.
/// </summary>
internal static class LocalExtensions
{
    /// <summary>
    /// WithLocalStackCredentials gives a container that uses a queue or a bucket LocalStack's
    /// test credentials, since it has no AWS sign-in. A process signs with the developer's
    /// AWS_PROFILE instead, which LocalStack takes as its one account, and must not get these:
    /// environment keys would win over the profile it reads protofast/dev with.
    /// </summary>
    public static IResourceBuilder<T> WithLocalStackCredentials<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEnvironment =>
        builder.ApplicationBuilder.ExecutionContext.IsRunMode
            ? builder.WithEnvironment("AWS_ACCESS_KEY_ID", "test").WithEnvironment("AWS_SECRET_ACCESS_KEY", "test")
            : builder;

    /// <summary>
    /// WithLocalOtlpExporter points a container's OpenTelemetry SDK at the dashboard, over
    /// OTLP/HTTP, as ProtoStrap points it at its collector. Projects get this from Aspire itself.
    /// </summary>
    public static IResourceBuilder<T> WithLocalOtlpExporter<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEnvironment =>
        builder.ApplicationBuilder.ExecutionContext.IsRunMode ? builder.WithOtlpExporter(OtlpProtocol.HttpProtobuf) : builder;

    /// <summary>
    /// WithLocalCollectorExport has the browsers' collector export to the dashboard, with the
    /// dashboard's OTLP key, which a collector's config can't read from OTEL_EXPORTER_OTLP_HEADERS.
    /// </summary>
    public static IResourceBuilder<T> WithLocalCollectorExport<T>(this IResourceBuilder<T> builder, IConfiguration configuration)
        where T : IResourceWithEnvironment =>
        builder.ApplicationBuilder.ExecutionContext.IsRunMode
            ? builder.WithOtlpExporter(OtlpProtocol.HttpProtobuf)
                .WithEnvironment("ASPIRE_OTLP_API_KEY", configuration["AppHost:OtlpApiKey"] ?? "")
            : builder;
}
