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
}
