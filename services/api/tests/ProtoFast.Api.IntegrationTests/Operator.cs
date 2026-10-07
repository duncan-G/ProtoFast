using System.Security.Claims;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.ServiceDefaults.InternalAuth;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>A caller as the internal JWT would describe it; each call runs in its own scope.</summary>
public sealed class Operator(StoryDatabase database, string tenant, params string[] roles)
{
    public async Task<TReply> Call<TService, TReply>(Func<TService, ServerCallContext, Task<TReply>> call)
        where TService : notnull
    {
        var claims = new List<Claim> { new("sub", $"operator-{Guid.NewGuid():N}"), new("tenant", tenant) };
        claims.AddRange(roles.Select(role => new Claim("roles", role)));

        var context = new TestServerCallContext();
        context.UserState[InternalJwtAuthInterceptor.PrincipalKey] = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        await using var scope = database.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>(), context);
    }
}
