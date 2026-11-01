using System.Collections;
using ProtoFast.ProtoStrap;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

/// <summary>
/// The origin auth builds redirect URIs from is the one ProtoStrap gives the request's host,
/// ORIGIN_&lt;NAME&gt;: https in an account, http on the local edge's port under `aspire run`.
/// Keycloak's realms list exactly these, so any other scheme or port is refused.
/// </summary>
public class PublicOriginTests
{
    private static readonly IDictionary Local = new Hashtable
    {
        ["HOST_PROTOFAST"] = "protofast.dev.localhost",
        ["ORIGIN_PROTOFAST"] = "http://protofast.dev.localhost:20000",
        ["HOST_ADMIN"] = "admin.dev.localhost",
        ["ORIGIN_ADMIN"] = "http://admin.dev.localhost:20000",
        ["PATH"] = "/usr/bin",
    };

    [Theory]
    [InlineData("protofast.dev.localhost:20000", "http://protofast.dev.localhost:20000")]
    [InlineData("PROTOFAST.dev.localhost", "http://protofast.dev.localhost:20000")]
    [InlineData("admin.dev.localhost:20000", "http://admin.dev.localhost:20000")]
    public void A_spec_host_gets_its_origin(string host, string origin)
    {
        Assert.Equal(origin, Replica.OriginFor(host, Local));
    }

    [Theory]
    [InlineData("elsewhere.example.com")]
    [InlineData("")]
    public void Any_other_host_has_none(string host)
    {
        Assert.Null(Replica.OriginFor(host, Local));
    }

    [Fact]
    public void A_host_without_an_origin_has_none()
    {
        Assert.Null(Replica.OriginFor("theplot.dev.localhost", new Hashtable { ["HOST_THEPLOT"] = "theplot.dev.localhost" }));
    }
}
