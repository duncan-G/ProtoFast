using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ProtoFast.ProtoStrap;
using ProtoFast.ServiceDefaults.Telemetry;

namespace ProtoFast.ServiceDefaults;

public static class Extensions
{
    // OIDC callback (auth service only). Keycloak's redirect starts a fresh request here, which the
    // framework would trace as a new root — a second, disconnected trace for the sign-in flow. The
    // auth service instead opens its own span parented to the /signin trace (AuthFlow.CallbackAsync),
    // so the auto span for this path is suppressed to keep the whole flow in one trace.
    private const string OidcCallbackPath = "/signin-oidc";
    private const string ActivitySourceName = "ProtoFast.*";

    /// <summary>
    /// AddServiceDefaults gives a component what ProtoStrap's application contract asks of it:
    /// its keys from the environment's secret, telemetry from the OTEL_ variables, readiness,
    /// and a drain on SIGTERM. <paramref name="stop"/> is how long stopping may take, inside
    /// the spec's <c>stop</c>: the drain plus the longest request or job. Without it, 8 s,
    /// inside ProtoStrap's default stop of 10 s.
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder, TimeSpan? stop = null)
    {
        builder.Configuration.AddProtoStrapSecret();

        builder.ConfigureOpenTelemetry();

        builder.Services.AddSingleton<DrainState>();
        builder.Services.AddHostedService<Drain>();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = stop ?? TimeSpan.FromSeconds(8));
        builder.Services.AddReadiness();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        // Under ProtoStrap logs leave by OTLP alone: a console logger would send each line twice.
        if (Replica.UnderProtoStrap)
        {
            builder.Logging.ClearProviders();
        }

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            // A fallback service.name for a run with neither ProtoStrap nor the AppHost, which
            // both set OTEL_SERVICE_NAME; the variable still wins.
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing
                    // Drop the Npgsql "SELECT 1" / Redis "PING" health-probe + keep-alive
                    // spans the Aspire integrations emit. Registered before the OTLP
                    // exporter (added last in AddOpenTelemetryExporters) so its batch
                    // processor sees these activities already marked un-recorded.
                    .AddProcessor(new HealthPingTraceFilter())
                    .AddSource(ActivitySourceName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Don't trace requests to the health endpoint to avoid filling the dashboard with noise
                        tracing.Filter = httpContext =>
                            !(httpContext.Request.Path.StartsWithSegments(Readiness.Path)
                              || httpContext.Request.Path.StartsWithSegments(Readiness.GrpcPath)
                              || httpContext.Request.Path.StartsWithSegments(OidcCallbackPath))
                    )
                    .AddHttpClientInstrumentation(options =>
                        options.FilterHttpRequestMessage = req =>
                            !string.Equals(req.RequestUri?.AbsolutePath, Readiness.Path, StringComparison.OrdinalIgnoreCase)
                            && !IsSqsReceive(req)
                    );
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    // Queue consumers long-poll continuously; each poll would otherwise export as its own root trace.
    // The consumer's process span carries the sender's trace instead.
    private static bool IsSqsReceive(HttpRequestMessage request) =>
        request.Headers.TryGetValues("X-Amz-Target", out var targets)
        && targets.Contains("AmazonSQS.ReceiveMessage", StringComparer.Ordinal);

    // The exporter reads OTEL_EXPORTER_OTLP_ENDPOINT and OTEL_EXPORTER_OTLP_PROTOCOL itself:
    // http/protobuf to ProtoStrap's collector in an account, gRPC to the dashboard locally.
    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return builder;
        }

        builder.Services
            .AddOpenTelemetry()
            .UseOtlpExporter();

        return builder;
    }

    /// <summary>
    /// MapDefaultEndpoints maps the readiness check, GET /healthz, and for a gRPC service
    /// (<paramref name="grpc"/>) gRPC's health service too.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app, bool grpc = false)
    {
        app.MapReadiness();
        if (grpc)
        {
            app.MapGrpcReadiness();
        }

        return app;
    }
}
