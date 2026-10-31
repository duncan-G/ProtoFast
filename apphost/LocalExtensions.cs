using Microsoft.Extensions.Configuration;

namespace ProtoFast.AppHost;

/// <summary>
/// LocalExtensions add what a resource needs under `aspire run` only. They add nothing to the
/// spec: in an account ProtoStrap gives each replica its credentials and telemetry endpoint.
/// </summary>
internal static class LocalExtensions
{
    /// <summary>
    /// WithLocalStackCredentials gives a component that uses a queue or a bucket LocalStack's
    /// test credentials, so the AWS SDK neither fails to find credentials nor signs with yours.
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

    /// <summary>
    /// LocalSecret is a secret parameter whose value the developer may keep in the AppHost's user
    /// secrets (<c>dotnet user-secrets set Parameters:&lt;name&gt; …</c>), and which is empty
    /// otherwise, so a run never stops to ask for it. In the spec it is only its key's name.
    /// </summary>
    public static IResourceBuilder<ParameterResource> AddLocalSecret(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddParameter(name, builder.Configuration[$"Parameters:{name}"] ?? "", secret: true);
}
