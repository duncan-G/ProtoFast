using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Tenancy;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

public class TenantResolverTests
{
    private static TenantResolver Resolver() => new(Options.Create(new TenantOptions
    {
        ByHost =
        {
            ["protofast.dev"] = new TenantConfig { Realm = "protofast", ClientId = "protofast-web" },
            ["admin.protofast.dev"] = new TenantConfig { Realm = "protofast", ClientId = "admin" },
            ["localhost"] = new TenantConfig { Realm = "protofast", ClientId = "protofast-web" },
        },
    }));

    [Fact]
    public void Resolves_exact_host()
    {
        Assert.True(Resolver().TryResolve("admin.protofast.dev", out var tenant));
        Assert.Equal("protofast", tenant!.Realm);
        Assert.Equal("admin", tenant.ClientId);
    }

    [Fact]
    public void Strips_port_then_falls_back_to_bare_host()
    {
        Assert.True(Resolver().TryResolve("localhost:20001", out var tenant));
        Assert.Equal("protofast-web", tenant!.ClientId);
    }

    [Fact]
    public void Is_case_insensitive()
    {
        Assert.True(Resolver().TryResolve("ProtoFast.DEV", out var tenant));
        Assert.Equal("protofast-web", tenant!.ClientId);
    }

    [Theory]
    [InlineData("myfitness.protofast.dev")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_or_empty_host_is_not_resolved(string? host)
    {
        Assert.False(Resolver().TryResolve(host, out var tenant));
        Assert.Null(tenant);
    }

    [Fact]
    public void Resolves_backwards_from_realm_and_client()
    {
        // Back-channel logout arrives from Keycloak, so there is no Host to map — the realm and
        // client come out of the logout token instead.
        Assert.True(Resolver().TryResolveByClient("protofast", "admin", out var tenant));
        Assert.Equal("admin", tenant!.ClientId);
        Assert.Equal("protofast", tenant.Realm);
    }

    [Theory]
    [InlineData("protofast", "not-a-client")]
    [InlineData("other-realm", "admin")]  // right client, wrong realm — never cross realms
    [InlineData("protofast", "")]
    [InlineData(null, "admin")]
    public void Unknown_realm_client_pair_is_not_resolved(string? realm, string? clientId)
    {
        Assert.False(Resolver().TryResolveByClient(realm, clientId, out var tenant));
        Assert.Null(tenant);
    }

    [Theory]
    [InlineData("protofast", true)]
    [InlineData("other-realm", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Knows_only_the_realms_it_holds_a_client_for(string? realm, bool known)
    {
        // Back-channel logout asks this before fetching a realm's signing keys, so "no" has to
        // mean no rather than falling through to a call to Keycloak.
        Assert.Equal(known, Resolver().KnowsRealm(realm));
    }
}
