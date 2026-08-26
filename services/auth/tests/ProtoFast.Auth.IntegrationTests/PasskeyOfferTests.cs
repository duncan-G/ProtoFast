using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>
/// /add-passkey is the only route by which a passkey is ever enrolled, so the two things that
/// would silently break it are worth pinning: reaching Keycloak at all, and asking for the right
/// action once there.
/// </summary>
public class PasskeyOfferTests(TestAuthWebApplicationFactory factory) : IClassFixture<TestAuthWebApplicationFactory>
{
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("http://protofast.dev"),
    });

    [Fact]
    public async Task Add_passkey_redirects_to_keycloak_with_the_enrolment_action()
    {
        var response = await GetAsync("/add-passkey?returnUrl=%2Fapp");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("kc_action=webauthn-register-passwordless", location, StringComparison.Ordinal);
        // The backstop for a stale local "has a passkey" flag; without it a user who enrolled
        // through Keycloak's own account console is asked to do it again.
        Assert.Contains("kc_action_parameter=skip_if_exists", location, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sign-up carries the offer on the registration authorize itself. Chaining it onto a second
    /// round trip — which is what sign-in does — would cost the brand-new account a second mailed
    /// code: registration records no authenticated level against the browser flow, so Keycloak
    /// answers the follow-up with "strong authentication required" and asks for a credential again.
    /// </summary>
    [Fact]
    public async Task Sign_up_carries_the_enrolment_action_on_its_own_authorize_request()
    {
        var response = await GetAsync("/signup");

        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("prompt=create", location, StringComparison.Ordinal);
        Assert.Contains("kc_action=webauthn-register-passwordless", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_in_does_not_carry_the_enrolment_action()
    {
        var response = await GetAsync("/signin");

        var location = response.Headers.Location?.ToString() ?? "";
        Assert.DoesNotContain("kc_action", location, StringComparison.Ordinal);
    }

    /// <summary>
    /// The product host asks for no particular level of authentication. Only the admin host does,
    /// and only once staff passkey coverage makes that safe — a stray acr_values here would demand
    /// a passkey of every user on the site.
    /// </summary>
    [Fact]
    public async Task Sign_in_on_the_product_host_asks_for_no_acr_and_no_max_age()
    {
        var response = await GetAsync("/signin");

        var location = response.Headers.Location?.ToString() ?? "";
        Assert.DoesNotContain("acr_values", location, StringComparison.Ordinal);
        Assert.DoesNotContain("max_age", location, StringComparison.Ordinal);
    }

    private Task<HttpResponseMessage> GetAsync(string path) => Client().GetAsync(path);
}
