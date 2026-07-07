using System.Security.Claims;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace ProtoFast.ServiceDefaults.InternalAuth;

/// <summary>
/// Rejects gRPC calls that lack a valid <c>x-internal-jwt</c> (guide §6). Since the edge only
/// annotates and never denies, this is the <b>primary</b> API enforcement point — the backend does
/// not trust the network. Health probes stay anonymous. A valid principal is stashed in
/// <see cref="ServerCallContext.UserState"/> under <see cref="PrincipalKey"/>.
/// </summary>
public sealed class InternalJwtAuthInterceptor(InternalJwtValidator validator) : Interceptor
{
    public const string PrincipalKey = "internal-principal";

    private const string HeaderName = "x-internal-jwt";
    private const string HealthServicePrefix = "/grpc.health.v1.Health/";

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(request, context);
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(requestStream, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(request, responseStream, context);
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(requestStream, responseStream, context);
    }

    private void Authorize(ServerCallContext context)
    {
        // Health probes (Envoy / grpc_health_probe) must stay anonymous.
        if (context.Method.StartsWith(HealthServicePrefix, StringComparison.Ordinal))
        {
            return;
        }

        var token = context.RequestHeaders.GetValue(HeaderName);
        var principal = validator.Validate(token);
        if (principal is null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid internal token."));
        }

        context.UserState[PrincipalKey] = principal;
    }

    /// <summary>Convenience accessor for the authenticated principal stashed by this interceptor.</summary>
    public static ClaimsPrincipal? GetPrincipal(ServerCallContext context) =>
        context.UserState.TryGetValue(PrincipalKey, out var value) ? value as ClaimsPrincipal : null;
}
