using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Identity;
using ProtoFast.Auth.Api.Keycloak;
using ProtoFast.Auth.Api.Sessions;
using ProtoFast.Auth.Api.Tenancy;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

/// <summary>
/// A session whose Keycloak tokens are dead must not survive in the store. It stays resolvable to
/// <c>/signin</c>, which then treats the user as signed in and redirects them back to the page the
/// SSR gate just bounced — a redirect loop. Only a failure Keycloak may recover from (5xx,
/// transport) is allowed to leave the session in place.
/// </summary>
public class SessionResolverTests
{
    private const string Host = "admin.protofast.dev";

    [Fact]
    public async Task Session_is_dropped_when_keycloak_rejects_the_refresh()
    {
        // Signing out on protofast.dev ends the shared realm's SSO session, so admin's refresh
        // comes back 400 invalid_grant "Session not active".
        var store = new FakeSessionStore();
        var sessionId = store.Seed(DeadAccessTokenSession());
        var resolver = Resolver(store, Rejects(HttpStatusCode.BadRequest));

        var identity = await resolver.ResolveSessionAsync(sessionId, Host, CancellationToken.None);

        Assert.Null(identity);
        Assert.Null(await store.GetAsync(sessionId, CancellationToken.None));
    }

    [Fact]
    public async Task Session_survives_a_refresh_failure_keycloak_may_recover_from()
    {
        var store = new FakeSessionStore();
        var sessionId = store.Seed(DeadAccessTokenSession());
        var resolver = Resolver(store, Rejects(HttpStatusCode.ServiceUnavailable));

        var identity = await resolver.ResolveSessionAsync(sessionId, Host, CancellationToken.None);

        Assert.Null(identity);
        Assert.NotNull(await store.GetAsync(sessionId, CancellationToken.None));
    }

    [Fact]
    public async Task Session_is_dropped_when_the_refresh_window_has_closed()
    {
        var store = new FakeSessionStore();
        var sessionId = store.Seed(DeadAccessTokenSession() with
        {
            RefreshExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        var keycloak = Rejects(HttpStatusCode.BadRequest);
        var resolver = Resolver(store, keycloak);

        var identity = await resolver.ResolveSessionAsync(sessionId, Host, CancellationToken.None);

        Assert.Null(identity);
        Assert.Null(await store.GetAsync(sessionId, CancellationToken.None));
        Assert.Equal(0, keycloak.RefreshCount); // a dead refresh token isn't worth a round-trip
    }

    [Fact]
    public async Task Live_session_resolves_and_is_left_alone()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var sessionId = store.Seed(DeadAccessTokenSession() with
        {
            AccessToken = SignedAccessToken(key, azp: "admin"),
        });
        var keycloak = Rejects(HttpStatusCode.BadRequest);
        keycloak.SigningKey = new ECDsaSecurityKey(key);
        var resolver = Resolver(store, keycloak);

        var identity = await resolver.ResolveSessionAsync(sessionId, Host, CancellationToken.None);

        Assert.NotNull(identity);
        Assert.Equal("user-123", identity!.Subject);
        Assert.NotNull(await store.GetAsync(sessionId, CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_session_id_resolves_to_anonymous()
    {
        var resolver = Resolver(new FakeSessionStore(), Rejects(HttpStatusCode.BadRequest));

        Assert.Null(await resolver.ResolveSessionAsync("no-such-id", Host, CancellationToken.None));
        Assert.Null(await resolver.ResolveSessionAsync(null, Host, CancellationToken.None));
    }

    private static SessionResolver Resolver(ISessionStore store, IKeycloakGateway keycloak) =>
        new(store,
            keycloak,
            new FakeJwtFactory(),
            new TenantResolver(Options.Create(new TenantOptions
            {
                ByHost = { [Host] = new TenantConfig { Realm = "protofast", ClientId = "admin" } },
            })),
            Options.Create(new SessionPolicyOptions()),
            TimeProvider.System,
            NullLogger<SessionResolver>.Instance);

    /// <summary>A session whose access token can never validate, forcing the refresh path.</summary>
    private static SessionData DeadAccessTokenSession() => new()
    {
        Sub = "user-123",
        Email = "a@b.com",
        Realm = "protofast",
        ClientId = "admin",
        Roles = ["admin"],
        AccessToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken()), // unsigned
        RefreshToken = "refresh-token",
        AccessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        RefreshExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
    };

    private static string SignedAccessToken(ECDsa key, string azp)
    {
        var jwt = new JwtSecurityToken(
            claims: [new Claim("sub", "user-123"), new Claim("azp", azp)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new ECDsaSecurityKey(key), SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static FakeKeycloakGateway Rejects(HttpStatusCode status) =>
        new(() => throw new KeycloakException($"Keycloak token endpoint returned {(int)status}.", status));

    private sealed class FakeSessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionData> _sessions = new(StringComparer.Ordinal);

        public string Seed(SessionData data)
        {
            var id = SessionIds.Generate();
            _sessions[id] = data;
            return id;
        }

        public Task<string> CreateAsync(SessionData data, CancellationToken ct = default) =>
            Task.FromResult(Seed(data));

        public Task<SessionData?> GetAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_sessions.GetValueOrDefault(sessionId));

        public Task DeleteAsync(string sessionId, CancellationToken ct = default)
        {
            _sessions.Remove(sessionId);
            return Task.CompletedTask;
        }

        public Task DeleteByKeycloakSessionAsync(string realm, string kcSessionId, CancellationToken ct = default)
        {
            foreach (var (id, data) in _sessions.ToArray())
            {
                if (data.Realm == realm && data.KcSessionId == kcSessionId)
                {
                    _sessions.Remove(id);
                }
            }

            return Task.CompletedTask;
        }

        public Task UpdateAsync(string sessionId, SessionData data, CancellationToken ct = default)
        {
            _sessions[sessionId] = data;
            return Task.CompletedTask;
        }

        public Task<string> ReplaceAsync(string oldSessionId, SessionData data, CancellationToken ct = default)
        {
            _sessions.Remove(oldSessionId);
            return Task.FromResult(Seed(data));
        }
    }

    private sealed class FakeKeycloakGateway(Func<KeycloakTokens> refresh) : IKeycloakGateway
    {
        public int RefreshCount { get; private set; }

        public SecurityKey? SigningKey { get; set; }

        public string BuildAuthorizeUrl(TenantConfig tenant, string redirectUri, string state, string codeChallenge, bool registration, string? kcAction = null) => "";

        public Task<KeycloakTokens> ExchangeCodeAsync(TenantConfig tenant, string code, string redirectUri, string codeVerifier, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KeycloakTokens> RefreshAsync(TenantConfig tenant, string refreshToken, CancellationToken ct = default)
        {
            RefreshCount++;
            return Task.FromResult(refresh());
        }

        public string BuildEndSessionUrl(TenantConfig tenant, string? idTokenHint, string postLogoutRedirectUri) => "";

        public Task<TokenValidationParameters> GetValidationParametersAsync(string realm, CancellationToken ct = default) =>
            Task.FromResult(new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                RequireSignedTokens = true,
                IssuerSigningKey = SigningKey,
                ClockSkew = TimeSpan.Zero,
            });
    }

    private sealed class FakeJwtFactory : IInternalJwtFactory
    {
        public InternalJwt Create(string subject, string tenant, IReadOnlyList<string> roles, bool subscribed = false) =>
            new("internal-jwt", DateTimeOffset.UtcNow.AddMinutes(5));
    }
}
