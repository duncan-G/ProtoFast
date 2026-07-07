using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Identity;
using ProtoFast.Auth.Api.Keycloak;
using ProtoFast.Auth.Api.Services;
using ProtoFast.Auth.Api.Sessions;
using ProtoFast.Auth.Api.Tenancy;
using ProtoFast.ServiceDefaults.InternalAuth;
using Xunit;

namespace ProtoFast.Auth.IntegrationTests;

public class HostWiringTests(TestAuthWebApplicationFactory factory) : IClassFixture<TestAuthWebApplicationFactory>
{
    [Fact]
    public void Auth_graph_resolves_from_the_real_host()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<ITenantResolver>());
        Assert.NotNull(sp.GetRequiredService<IKeycloakGateway>());
        Assert.NotNull(sp.GetRequiredService<IInternalJwtFactory>());
        Assert.NotNull(sp.GetRequiredService<SessionResolver>());

        // The gRPC service is activated per-call (not a DI registration); prove its deps resolve.
        Assert.NotNull(ActivatorUtilities.CreateInstance<AuthorizationService>(sp));
    }

    [Fact]
    public async Task Request_without_a_cookie_resolves_to_anonymous()
    {
        var resolver = factory.Services.GetRequiredService<SessionResolver>();

        var identity = await resolver.ResolveAsync(cookieHeader: null, host: "protofast.dev", CancellationToken.None);

        Assert.Null(identity);
    }

    [Fact]
    public void Host_signing_key_is_trusted_by_the_matching_public_key()
    {
        var minted = factory.Services.GetRequiredService<IInternalJwtFactory>()
            .Create("user-123", "protofast", ["admin"]);

        var validator = new InternalJwtValidator(Options.Create(new InternalJwtValidationOptions
        {
            PublicKeyPem = factory.InternalJwtPublicKeyPem,
        }));

        var principal = validator.Validate(minted.Token);

        Assert.NotNull(principal);
        Assert.Equal("user-123", principal!.FindFirst("sub")?.Value);
    }
}
