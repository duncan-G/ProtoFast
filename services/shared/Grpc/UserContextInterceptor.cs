using Grpc.Core;
using Grpc.Core.Interceptors;
using ProtoFast.Database.Abstractions;
using ProtoFast.ServiceDefaults.InternalAuth;

namespace ProtoFast.Grpc;

/// <summary>
/// Confines data access for the duration of a call to the caller the internal JWT names. Runs
/// after <see cref="InternalJwtAuthInterceptor"/> and reads the subject the same way
/// <see cref="CallerIdentity"/> does. A call with no principal (health probes) sets nothing, so
/// any scoped query it made would fail closed.
/// </summary>
public sealed class UserContextInterceptor(UserContext userContext) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        using IDisposable? scope = BeginUserScope(context);
        return await continuation(request, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        using IDisposable? scope = BeginUserScope(context);
        return await continuation(requestStream, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        using IDisposable? scope = BeginUserScope(context);
        await continuation(request, responseStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        using IDisposable? scope = BeginUserScope(context);
        await continuation(requestStream, responseStream, context);
    }

    private IDisposable? BeginUserScope(ServerCallContext context)
    {
        if (InternalJwtAuthInterceptor.GetPrincipal(context) is null)
        {
            return null;
        }

        return userContext.SetCurrentUser(CallerIdentity.From(context).Subject);
    }
}
