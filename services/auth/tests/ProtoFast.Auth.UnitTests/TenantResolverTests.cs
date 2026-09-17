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
            ["theplot.protofast.dev"] = new TenantConfig { Realm = "theplot", ClientId = "theplot-web" },
            // Dev puts every client on localhost, so the port is the only thing separating them.
            // Keyed by label with an explicit Host, matching the shape configuration can express:
            // a ":" in the key would bind as a nested section. TenantConfigBindingTests pins that.
            ["theplot-dev"] = new TenantConfig
            {
                Host = "localhost:20002", Realm = "theplot", ClientId = "theplot-web",
            },
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
    public void Port_specific_entry_beats_the_bare_host()
    {
        // The bug this guards: with the port dropped before the lookup, ThePlot on :20002
        // resolved to the "localhost" entry and got protofast-web. Keycloak then rejected the
        // sign-in, because https://localhost:20002/signin-oidc is not a protofast-web redirect
        // URI — an invalid_redirect_uri error rather than anything naming the real cause.
        Assert.True(Resolver().TryResolve("localhost:20002", out var tenant));
        Assert.Equal("theplot", tenant!.Realm);
        Assert.Equal("theplot-web", tenant.ClientId);
    }

    [Fact]
    public void Port_specific_entry_does_not_leak_to_other_ports()
    {
        // :20000 and :20001 have no entry of their own and must still land on the bare host.
        Assert.True(Resolver().TryResolve("localhost:20000", out var tenant));
        Assert.Equal("protofast", tenant!.Realm);
        Assert.Equal("protofast-web", tenant.ClientId);
    }

    [Fact]
    public void Resolves_a_second_realm_by_host()
    {
        Assert.True(Resolver().TryResolve("theplot.protofast.dev", out var tenant));
        Assert.Equal("theplot", tenant!.Realm);
        Assert.Equal("theplot-web", tenant.ClientId);
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

    [Fact]
    public void Resolves_the_second_realm_backwards_too()
    {
        // ThePlot's realm has to round-trip as well, or a back-channel logout for it is refused.
        Assert.True(Resolver().TryResolveByClient("theplot", "theplot-web", out var tenant));
        Assert.Equal("theplot", tenant!.Realm);
    }

    [Theory]
    [InlineData("protofast", true)]
    [InlineData("theplot", true)]
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
