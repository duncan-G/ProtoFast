using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Tenancy;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

/// <summary>
/// Binds tenants the way the running service does — through the configuration system — rather
/// than by constructing <see cref="TenantOptions"/> in code. The difference is the whole point:
/// configuration keys are colon-delimited, so a host written with a port in the KEY is read as a
/// nested section and the entry disappears silently. Tests that build the options directly cannot
/// see that, and did not.
/// </summary>
public class TenantConfigBindingTests
{
    private static TenantResolver Bind(string json)
    {
        var config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();

        var options = new TenantOptions();
        config.GetSection("Tenants").Bind(options);
        return new TenantResolver(Options.Create(options));
    }

    [Fact]
    public void Host_field_carries_a_port_that_the_key_cannot()
    {
        var resolver = Bind("""
        {
          "Tenants": {
            "ByHost": {
              "localhost": { "Realm": "protofast", "ClientId": "protofast-web" },
              "theplot-dev": { "Host": "localhost:20002", "Realm": "theplot", "ClientId": "theplot-web" }
            }
          }
        }
        """);

        Assert.True(resolver.TryResolve("localhost:20002", out var tenant));
        Assert.Equal("theplot", tenant!.Realm);
        Assert.Equal("theplot-web", tenant.ClientId);

        // The bare host still answers for every other port.
        Assert.True(resolver.TryResolve("localhost:20001", out var other));
        Assert.Equal("protofast", other!.Realm);
    }

    [Fact]
    public void A_port_in_the_key_does_not_survive_binding()
    {
        // Pinning the trap rather than the workaround: ":" splits the key into a subsection, so
        // this entry is not a host mapping at all and :20002 falls through to "localhost".
        var resolver = Bind("""
        {
          "Tenants": {
            "ByHost": {
              "localhost": { "Realm": "protofast", "ClientId": "protofast-web" },
              "localhost:20002": { "Realm": "theplot", "ClientId": "theplot-web" }
            }
          }
        }
        """);

        Assert.True(resolver.TryResolve("localhost:20002", out var tenant));
        Assert.Equal("protofast", tenant!.Realm);
    }

    [Fact]
    public void Two_entries_claiming_one_host_fail_loudly()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Bind("""
        {
          "Tenants": {
            "ByHost": {
              "localhost": { "Realm": "protofast", "ClientId": "protofast-web" },
              "dupe": { "Host": "LOCALHOST", "Realm": "theplot", "ClientId": "theplot-web" }
            }
          }
        }
        """));

        Assert.Contains("localhost", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shipped_dev_map_routes_each_client_port_to_its_own_realm()
    {
        // Guards the real appsettings.Development.json (linked into the test output), because
        // this is the file that decides which realm ThePlot's sign-in reaches locally.
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Development.json")
            .Build();

        var options = new TenantOptions();
        config.GetSection("Tenants").Bind(options);
        var resolver = new TenantResolver(Options.Create(options));

        Assert.True(resolver.TryResolve("localhost:20002", out var theplot));
        Assert.Equal("theplot", theplot!.Realm);
        Assert.Equal("theplot-web", theplot.ClientId);

        Assert.True(resolver.TryResolve("localhost:20001", out var protofast));
        Assert.Equal("protofast", protofast!.Realm);
        Assert.Equal("protofast-web", protofast.ClientId);
    }
}
