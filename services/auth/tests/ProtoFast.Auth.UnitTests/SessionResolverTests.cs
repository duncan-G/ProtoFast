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
    public async Task Concurrent_requests_on_an_expired_token_share_one_refresh()
    {
        // A page load sends several requests on the same cookie just after the access token
        // lapses. Keycloak takes the refresh token once; a second refresh would be refused and
        // read as a dead session.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeSessionStore();
        var sessionId = store.Seed(DeadAccessTokenSession());
        var keycloak = SingleUseRefreshTokens(key, release, entered);
        var resolver = Resolver(store, keycloak);

        var first = resolver.ResolveSessionAsync(sessionId, Host, CancellationToken.None);
        await entered.Task;
        var second = resolver.ResolveSessionAsync(sessionId, Host, CancellationToken.None);
        release.SetResult();

        var identities = await Task.WhenAll(first, second);

        Assert.All(identities, identity => Assert.NotNull(identity));
        Assert.NotNull(identities[0]!.RotatedSessionId);
        Assert.Equal(identities[0]!.RotatedSessionId, identities[1]!.RotatedSessionId);
        Assert.Equal(1, keycloak.RefreshCount);
    }

    [Fact]
    public async Task Old_id_follows_the_rotation_instead_of_refreshing_again()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var release = new TaskCompletionSource();
        release.SetResult();
        var store = new FakeSessionStore();
        var sessionId = store.Seed(DeadAccessTokenSession());
        var keycloak = SingleUseRefreshTokens(key, release, new TaskCompletionSource());
        var resolver = Resolver(store, keycloak);

        var refreshed = await resolver.ResolveSessionAsync(sessionId, Host, CancellationToken.None);
        var late = await resolver.ResolveSessionAsync(sessionId, Host, CancellationToken.None);

        Assert.NotNull(late);
        Assert.Equal(refreshed!.RotatedSessionId, late!.RotatedSessionId);
        Assert.Equal(1, keycloak.RefreshCount);
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

    /// <summary>Keycloak with <c>refreshTokenMaxReuse: 0</c>: each refresh token works once, and
    /// every refresh is held until <see cref="Release"/> so tests can pile requests up behind it.</summary>
    private static FakeKeycloakGateway SingleUseRefreshTokens(ECDsa key, TaskCompletionSource release, TaskCompletionSource entered)
    {
        var spent = new HashSet<string>(StringComparer.Ordinal);
        var issued = 0;
        return new FakeKeycloakGateway(async refreshToken =>
        {
            lock (spent)
            {
                if (!spent.Add(refreshToken))
                {
                    throw new KeycloakException("Keycloak token endpoint returned 400.", HttpStatusCode.BadRequest);
                }
            }

            entered.TrySetResult();
            await release.Task;
            return new KeycloakTokens(
                SignedAccessToken(key, azp: "admin"),
                $"refresh-token-{Interlocked.Increment(ref issued)}",
                null,
                DateTimeOffset.UtcNow.AddMinutes(5),
                DateTimeOffset.UtcNow.AddMinutes(30));
        })
        {
            SigningKey = new ECDsaSecurityKey(key),
        };
    }

    /// <summary>Rotates on refresh the way the Redis store does: the old record goes, a pointer to
    /// the new id stays.</summary>
    private sealed class FakeSessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionData> _sessions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _successors = new(StringComparer.Ordinal);
        private readonly HashSet<string> _refreshLocks = new(StringComparer.Ordinal);

        public string Seed(SessionData data)
        {
            var id = SessionIds.Generate();
            lock (_sessions)
            {
                _sessions[id] = data;
            }

            return id;
        }

        public Task<string> CreateAsync(SessionData data, CancellationToken ct = default) =>
            Task.FromResult(Seed(data));

        public Task<SessionData?> GetAsync(string sessionId, CancellationToken ct = default)
        {
            lock (_sessions)
            {
                return Task.FromResult(_sessions.GetValueOrDefault(sessionId));
            }
        }

        public Task DeleteAsync(string sessionId, CancellationToken ct = default)
        {
            lock (_sessions)
            {
                _sessions.Remove(sessionId);
            }

            return Task.CompletedTask;
        }

        public Task DeleteByKeycloakSessionAsync(string realm, string kcSessionId, CancellationToken ct = default)
        {
            lock (_sessions)
            {
                foreach (var (id, data) in _sessions.ToArray())
                {
                    if (data.Realm == realm && data.KcSessionId == kcSessionId)
                    {
                        _sessions.Remove(id);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task UpdateAsync(string sessionId, SessionData data, CancellationToken ct = default)
        {
            lock (_sessions)
            {
                _sessions[sessionId] = data;
            }

            return Task.CompletedTask;
        }

        public Task<string> ReplaceAsync(string oldSessionId, SessionData data, CancellationToken ct = default)
        {
            var newId = Seed(data);
            lock (_sessions)
            {
                _successors[oldSessionId] = newId;
                _sessions.Remove(oldSessionId);
            }

            return Task.FromResult(newId);
        }

        public Task<string?> GetSuccessorAsync(string sessionId, CancellationToken ct = default)
        {
            lock (_sessions)
            {
                return Task.FromResult(_successors.GetValueOrDefault(sessionId));
            }
        }

        public Task<string?> TryLockRefreshAsync(string sessionId, TimeSpan expiry, CancellationToken ct = default)
        {
            lock (_refreshLocks)
            {
                return Task.FromResult(_refreshLocks.Add(sessionId) ? sessionId : null);
            }
        }

        public Task ReleaseRefreshLockAsync(string sessionId, string lockToken, CancellationToken ct = default)
        {
            lock (_refreshLocks)
            {
                _refreshLocks.Remove(sessionId);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeKeycloakGateway(Func<string, Task<KeycloakTokens>> refresh) : IKeycloakGateway
    {
        private int _refreshCount;

        public FakeKeycloakGateway(Func<KeycloakTokens> refresh)
            : this(_ => Task.FromResult(refresh()))
        {
        }

        public int RefreshCount => _refreshCount;

        public SecurityKey? SigningKey { get; set; }

        public string BuildAuthorizeUrl(TenantConfig tenant, string redirectUri, string state, string codeChallenge, bool registration, string? kcAction = null) => "";

        public Task<KeycloakTokens> ExchangeCodeAsync(TenantConfig tenant, string code, string redirectUri, string codeVerifier, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KeycloakTokens> RefreshAsync(TenantConfig tenant, string refreshToken, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _refreshCount);
            return refresh(refreshToken);
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
