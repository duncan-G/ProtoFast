using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Auth.Api.Sessions;
using Xunit;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>
/// The redirect-loop guard on /signin. The SSR gate sends every unannotated request here, so
/// /signin may only bounce the browser back when the session genuinely still works — a session
/// whose Keycloak tokens are dead has to start a fresh flow instead.
/// </summary>
public class SignInLoopTests(TestAuthWebApplicationFactory factory) : IClassFixture<TestAuthWebApplicationFactory>
{
    private const string AuthorizeUrl = "https://auth.protofast.test/realms/protofast/protocol/openid-connect/auth";

    [Fact]
    public async Task Signin_does_not_bounce_back_when_the_session_cannot_be_resolved()
    {
        var sessionId = Seed(StaleSession());

        var response = await GetSignInAsync("/signin?returnUrl=%2Fapp", sessionId);

        // The bug: /signin trusted the session record's mere existence and redirected straight back
        // to /app, which the SSR gate then bounced here again.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(AuthorizeUrl, response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signin_clears_the_cookie_of_a_session_that_resolves_to_nothing()
    {
        // A realm the host no longer maps to is rejected before any Keycloak round-trip, so this
        // reaches the resolved-to-anonymous branch rather than the unreachable-Keycloak one.
        var sessionId = Seed(StaleSession() with { Realm = "retired-realm" });

        var response = await GetSignInAsync("/signin", sessionId);

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("pf_session=;", setCookie, StringComparison.Ordinal);
        Assert.StartsWith(AuthorizeUrl, response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signin_without_a_cookie_goes_to_keycloak()
    {
        var response = await GetSignInAsync("/signin", sessionId: null);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(AuthorizeUrl, response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    private string Seed(SessionData session) =>
        factory.Services.GetRequiredService<StubSessionStore>().Seed(session);

    private async Task<HttpResponseMessage> GetSignInAsync(string path, string? sessionId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://protofast.dev"),
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (sessionId is not null)
        {
            request.Headers.Add("Cookie", $"pf_session={sessionId}");
        }

        return await client.SendAsync(request, CancellationToken.None);
    }

    /// <summary>A session record that outlived its Keycloak tokens — what a sign-out on a sibling
    /// host in the same realm leaves behind. Keycloak is unreachable in these tests, so resolving
    /// it fails exactly as it does in production once the SSO session is gone.</summary>
    private static SessionData StaleSession() => new()
    {
        Sub = "user-123",
        Email = "a@b.com",
        Realm = "protofast",
        ClientId = "protofast-web",
        Roles = [],
        AccessToken = "dead",
        RefreshToken = "dead",
        AccessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        RefreshExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
    };
}
