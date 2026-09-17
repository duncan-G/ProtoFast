using System.Net;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>
/// ThePlot has its own Keycloak realm, so a sign-in started on its host must reach
/// <c>/realms/theplot</c> with ThePlot's client and ThePlot's redirect URI. Getting any of the
/// three from the protofast tenant is what produced Keycloak's <c>invalid_redirect_uri</c>: the
/// authorize request asked protofast-web to accept an origin only theplot-web is registered for.
/// </summary>
public class TheplotRealmRoutingTests(TestAuthWebApplicationFactory factory)
    : IClassFixture<TestAuthWebApplicationFactory>
{
    // The dev listener ThePlot answers on. The port is load-bearing: it is the only thing
    // separating the three clients locally, since they all live on localhost.
    private const string TheplotOrigin = "http://localhost:20002";

    [Theory]
    [InlineData("/signin?returnUrl=%2Fapp")]
    [InlineData("/signup")]
    public async Task Theplot_host_starts_the_flow_in_the_theplot_realm(string path)
    {
        var response = await GetAsync(TheplotOrigin, path);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";

        Assert.StartsWith(
            "https://auth.protofast.test/realms/theplot/protocol/openid-connect/auth",
            location,
            StringComparison.Ordinal);

        var query = HttpUtility.ParseQueryString(new Uri(location).Query);
        Assert.Equal("theplot-web", query["client_id"]);
        // Built from the preserved Host, port and all — this is the value Keycloak was rejecting.
        Assert.Equal("https://localhost:20002/signin-oidc", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
    }

    [Fact]
    public async Task Protofast_host_is_unaffected()
    {
        var response = await GetAsync("http://protofast.dev", "/signin");

        var location = response.Headers.Location?.ToString() ?? "";
        Assert.StartsWith(
            "https://auth.protofast.test/realms/protofast/protocol/openid-connect/auth",
            location,
            StringComparison.Ordinal);
        Assert.Equal("protofast-web", HttpUtility.ParseQueryString(new Uri(location).Query)["client_id"]);
    }

    private async Task<HttpResponseMessage> GetAsync(string origin, string path)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(origin),
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await client.SendAsync(request, CancellationToken.None);
    }
}
