using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProtoFast.ProtoStrap;

namespace ProtoFast.ServiceDefaults;

/// <summary>
/// Readiness answers ProtoStrap's checks (its docs/protostrap.md#readiness-checks): ready only
/// when asked for this component by name, since a stopped replica's overlay address may be
/// another service's next; only while not draining; and only while what the component can't
/// work without (PostgreSQL, Redis: the Aspire integrations' checks) answers.
/// </summary>
public static class Readiness
{
    /// <summary>Path is the HTTP check's path, the spec's <c>ready: /healthz</c>.</summary>
    public const string Path = "/healthz";

    /// <summary>GrpcPath is the gRPC health service's path, the spec's <c>ready: grpc</c>.</summary>
    public const string GrpcPath = "/grpc.health.v1.Health";

    /// <summary>
    /// AddReadiness adds the drain check to the health checks, and maps gRPC's health service
    /// for "" and the component's name to all of them, run on every call: ProtoStrap's edge
    /// asks for the component by name, and NOT_FOUND answers any other name.
    /// </summary>
    public static IServiceCollection AddReadiness(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            .AddCheck<DrainCheck>("draining");
        services.AddGrpcHealthChecks(options =>
        {
            options.UseHealthChecksCache = false;
            if (Replica.Component is { } component)
                options.Services.Map(component, _ => true);
        });
        return services;
    }

    /// <summary>
    /// MapGrpcReadiness maps gRPC's health service, for a gRPC service's <c>ready: grpc</c>. It
    /// needs <c>AddGrpc</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapGrpcReadiness(this IEndpointRouteBuilder app)
    {
        app.MapGrpcHealthChecksService();
        return app;
    }

    /// <summary>
    /// MapReadiness maps GET /healthz, 200 when ready and 503 otherwise. Outside ProtoStrap the
    /// host isn't tested, so local checks pass.
    /// </summary>
    public static IEndpointRouteBuilder MapReadiness(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, async (HttpContext context, HealthCheckService health) =>
        {
            if (!ForComponent(context.Request.Host.Host, Replica.Component))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            var report = await health.CheckHealthAsync(context.RequestAborted);
            return report.Status == HealthStatus.Healthy ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        });
        return app;
    }

    /// <summary>ForComponent says whether a check's host names the component, or there is none, as locally.</summary>
    public static bool ForComponent(string host, string? component) =>
        component is null || string.Equals(host, component, StringComparison.OrdinalIgnoreCase);

    private sealed class DrainCheck(DrainState drain) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(drain.Draining ? HealthCheckResult.Unhealthy("draining") : HealthCheckResult.Healthy());
    }
}
