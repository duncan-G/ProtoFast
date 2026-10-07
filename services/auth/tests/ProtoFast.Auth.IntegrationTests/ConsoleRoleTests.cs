using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Correlation;
using ProtoFast.Auth.Api.Keycloak;
using Xunit;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>Only an operator holding a console role gets a session on the admin host.</summary>
public class ConsoleRoleTests(TestAuthWebApplicationFactory factory) : IClassFixture<TestAuthWebApplicationFactory>
{
    private const string ConsoleHost = "admin.protofast.test";

    [Fact]
    public async Task Callback_without_a_console_role_issues_no_session()
    {
        var app = Console(roles: ["offline_access", "uma_authorization"]);
        await app.Services.GetRequiredService<ICorrelationStore>().SaveAsync(
            "state-1",
            new CorrelationData("verifier", $"https://{ConsoleHost}/signin-oidc", "/", "operators", "admin"),
            TestContext.Current.CancellationToken);

        var response = await app
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri($"http://{ConsoleHost}"),
            })
            .GetAsync("/signin-oidc?state=state-1&code=code-1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location?.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Signin_on_the_admin_host_goes_to_the_operators_realm()
    {
        var response = await Console(roles: [])
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri($"http://{ConsoleHost}"),
            })
            .GetAsync("/signin", TestContext.Current.CancellationToken);

        var location = response.Headers.Location?.ToString() ?? "";
        Assert.StartsWith("https://auth.protofast.test/realms/operators/", location, StringComparison.Ordinal);
        Assert.Contains("client_id=admin", location, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> Console(string[] roles) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"Tenants:ByHost:{ConsoleHost}:Realm"] = "operators",
                    [$"Tenants:ByHost:{ConsoleHost}:ClientId"] = "admin",
                    [$"Tenants:ByHost:{ConsoleHost}:RequiredRoles:0"] = "platform",
                    [$"Tenants:ByHost:{ConsoleHost}:RequiredRoles:1"] = "admin-theplot",
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IKeycloakGateway>();
                services.AddSingleton<KeycloakGateway>();
                services.AddSingleton<IKeycloakGateway>(sp =>
                    new IssuingGateway(sp.GetRequiredService<KeycloakGateway>(), roles));
            });
        });

    /// <summary>The real gateway for URLs, with a code exchange that issues the given realm roles.</summary>
    private sealed class IssuingGateway(IKeycloakGateway inner, string[] roles) : IKeycloakGateway
    {
        public string BuildAuthorizeUrl(TenantConfig tenant, string redirectUri, string state, string codeChallenge, bool registration, string? kcAction = null) =>
            inner.BuildAuthorizeUrl(tenant, redirectUri, state, codeChallenge, registration, kcAction);

        public Task<KeycloakTokens> ExchangeCodeAsync(TenantConfig tenant, string code, string redirectUri, string codeVerifier, CancellationToken ct = default)
        {
            var access = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims:
            [
                new Claim("sub", "operator-1"),
                new Claim("email", "op@example.com"),
                new Claim("realm_access", JsonSerializer.Serialize(new { roles }), JsonClaimValueTypes.Json),
            ]));
            return Task.FromResult(new KeycloakTokens(
                access, "refresh", null, DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow.AddMinutes(30)));
        }

        public Task<KeycloakTokens> RefreshAsync(TenantConfig tenant, string refreshToken, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public string BuildEndSessionUrl(TenantConfig tenant, string? idTokenHint, string postLogoutRedirectUri) =>
            inner.BuildEndSessionUrl(tenant, idTokenHint, postLogoutRedirectUri);

        public Task<TokenValidationParameters> GetValidationParametersAsync(string realm, CancellationToken ct = default) =>
            inner.GetValidationParametersAsync(realm, ct);
    }
}
