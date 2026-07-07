using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ProtoFast.ServiceDefaults.Telemetry;

namespace ProtoFast.ServiceDefaults;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    // gRPC Health service path hit every 10s by grpc_health_probe (the production
    // healthcheck). StartsWithSegments matches both /Check and /Watch under it.
    private const string GrpcHealthEndpointPath = "/grpc.health.v1.Health";
    // OIDC callback (auth service only). Keycloak's redirect starts a fresh request here, which the
    // framework would trace as a new root — a second, disconnected trace for the sign-in flow. The
    // auth service instead opens its own span parented to the /signin trace (AuthFlow.CallbackAsync),
    // so the auto span for this path is suppressed to keep the whole flow in one trace.
    private const string OidcCallbackPath = "/signin-oidc";
    private const string ActivitySourceName = "ProtoFast.*";

    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            // Set service.name on the resource so telemetry is attributed to this service
            // in every environment. In dev the Aspire AppHost injects OTEL_SERVICE_NAME per
            // project, but in production (plain docker-compose, no AppHost) nothing sets it,
            // so without this the SDK falls back to "unknown_service:dotnet". An explicit
            // OTEL_SERVICE_NAME env var still overrides this value.
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
                            !(httpContext.Request.Path.StartsWithSegments(HealthEndpointPath)
                              || httpContext.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                              || httpContext.Request.Path.StartsWithSegments(GrpcHealthEndpointPath)
                              || httpContext.Request.Path.StartsWithSegments(OidcCallbackPath))
                    )
                    .AddHttpClientInstrumentation(options =>
                        options.FilterHttpRequestMessage = req =>
                            !string.Equals(req.RequestUri?.AbsolutePath, "/health", StringComparison.OrdinalIgnoreCase)
                    );
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        if (builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is not { } endpoint)
        {
            Console.WriteLine("OTEL_EXPORTER_OTLP_ENDPOINT is not set, skipping OpenTelemetry exporters");
            return builder;
        }


        builder.Services
            .AddOpenTelemetry()
            .UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri(endpoint));

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        // Surface the same health checks over the standard gRPC Health protocol so the
        // production deploy script can probe these gRPC services with grpc_health_probe.
        builder.Services.AddGrpcHealthChecks();

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // The gRPC Health service is the production probe (consumed by grpc_health_probe in
        // the deploy health-check loop), so it is mapped in all environments. It only exposes
        // SERVING/NOT_SERVING status, not the detailed HTTP health report below.
        app.MapGrpcHealthChecksService();

        // The detailed HTTP /health + /alive endpoints have security implications in
        // non-development environments (they can leak check names/details).
        // See https://aka.ms/dotnet/aspire/healthchecks before enabling them in production.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}
