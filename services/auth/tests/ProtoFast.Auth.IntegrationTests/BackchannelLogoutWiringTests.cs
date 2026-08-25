using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>
/// The endpoint is reachable on the real host and refuses anything it cannot verify. Token
/// validation itself is covered in the unit suite, where the realm signing key can be controlled;
/// Keycloak is deliberately unreachable here, so these assert routing and the refusals that need
/// no key.
/// </summary>
public class BackchannelLogoutWiringTests(TestAuthWebApplicationFactory factory)
    : IClassFixture<TestAuthWebApplicationFactory>
{
    [Fact]
    public async Task Endpoint_is_routed_and_refuses_a_token_it_cannot_read()
    {
        var response = await PostAsync(new Dictionary<string, string> { ["logout_token"] = "not-a-jwt" });

        // 400, not 404: the route exists and the handler resolved out of the real DI graph.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken);
        Assert.Equal("invalid_request", body?.Error);
    }

    [Fact]
    public async Task Missing_token_is_refused()
    {
        var response = await PostAsync(new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Issuer_outside_a_realm_is_refused_before_any_keycloak_call()
    {
        // A JWT whose `iss` names no realm can't select validation parameters, so it has to die
        // here rather than sending us to fetch keys from an attacker-chosen authority.
        var response = await PostAsync(new Dictionary<string, string>
        {
            ["logout_token"] = UnsignedJwt("""{"iss":"https://evil.example.com/","sid":"sso-1"}"""),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Responses_are_never_cached()
    {
        var response = await PostAsync(new Dictionary<string, string> { ["logout_token"] = "not-a-jwt" });

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Endpoint_does_not_answer_GET()
    {
        // The browser-facing OIDC endpoints are all GET; this one is only ever a server-to-server
        // POST, so a stray link or prefetch can never reach it.
        var response = await Client().GetAsync("/backchannel-logout", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private Task<HttpResponseMessage> PostAsync(Dictionary<string, string> form) =>
        Client().PostAsync(
            "/backchannel-logout",
            new FormUrlEncodedContent(form),
            TestContext.Current.CancellationToken);

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("http://protofast.dev"),
    });

    /// <summary>An <c>alg=none</c> JWT — well formed enough to be read, never to validate.</summary>
    private static string UnsignedJwt(string payloadJson) =>
        $"{Base64Url("""{"alg":"none","typ":"JWT"}""")}.{Base64Url(payloadJson)}.";

    private static string Base64Url(string value) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record ErrorBody(string Error);
}
