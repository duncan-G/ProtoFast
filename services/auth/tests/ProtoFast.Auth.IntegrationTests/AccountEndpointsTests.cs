using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>
/// The account endpoints run with ext_authz OFF, which means nothing upstream vouches for the
/// caller and nothing strips the identity headers Envoy would otherwise inject. Two things
/// therefore have to hold whatever else changes: an unauthenticated caller gets nothing, and a
/// caller cannot talk their way in with headers of their own.
/// </summary>
public class AccountEndpointsTests(TestAuthWebApplicationFactory factory)
    : IClassFixture<TestAuthWebApplicationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("http://protofast.dev"),
    });

    [Fact]
    public async Task Reading_the_account_without_a_session_is_unauthorized()
    {
        var response = await Client().GetAsync("/account/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The headers ext_authz injects downstream mean nothing here: on this route the client is
    /// the only thing that could have set them. Only the session cookie counts.
    /// </summary>
    [Fact]
    public async Task Injected_identity_headers_do_not_authenticate_an_account_request()
    {
        var client = Client();
        client.DefaultRequestHeaders.Add("x-user-id", "user-123");
        client.DefaultRequestHeaders.Add("x-tenant", "protofast");
        client.DefaultRequestHeaders.Add("x-authenticated", "true");

        var response = await client.GetAsync("/account/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Removing_a_passkey_without_a_session_is_unauthorized()
    {
        var response = await Client().DeleteAsync("/account/passkeys/cred-1", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_without_a_session_is_unauthorized()
    {
        var response = await Client().PostAsync("/account/delete", content: null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The session cookie is SameSite=Lax, so a cross-site write never carries it anyway — this
    /// is the belt to that braces, and it is refused before the session is even looked at.
    /// </summary>
    [Theory]
    [InlineData("POST", "/account/delete")]
    [InlineData("DELETE", "/account/passkeys/cred-1")]
    [InlineData("POST", "/account/email")]
    [InlineData("POST", "/account/email/confirm")]
    [InlineData("DELETE", "/account/email")]
    public async Task A_write_from_another_origin_is_refused(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Origin", "https://evil.example");

        var response = await Client().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The email change is three endpoints of ours now, not a hand-off to Keycloak's console, so
    /// each one has to be as closed to an anonymous caller as the rest of the group.
    /// </summary>
    [Theory]
    [InlineData("POST", "/account/email")]
    [InlineData("POST", "/account/email/confirm")]
    [InlineData("DELETE", "/account/email")]
    public async Task Changing_email_without_a_session_is_unauthorized(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { newEmail = "new@example.com", code = "123456" }),
        };

        var response = await Client().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The body is read inside the handler, after the session check, so a caller with no session
    /// cannot tell a rejected body apart from a rejected session — and cannot reach the parser at
    /// all.
    /// </summary>
    [Fact]
    public async Task An_unparseable_body_is_still_only_unauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/email")
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json"),
        };

        var response = await Client().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
