using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Endpoints;
using ProtoFast.Auth.Api.Keycloak;
using ProtoFast.Auth.Api.Security;
using ProtoFast.Auth.Api.Sessions;
using ProtoFast.Auth.Api.Tenancy;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

/// <summary>
/// The back-channel logout endpoint is the one place Keycloak reaches into this service, and it is
/// unauthenticated apart from the token itself — so every check that makes the token trustworthy
/// is load-bearing. The happy path proves a logout on one host clears the sibling host's session,
/// which is the whole reason the endpoint exists.
/// </summary>
public class BackchannelLogoutTests
{
    private const string Realm = "protofast";
    private const string Issuer = $"https://auth.protofast.test/realms/{Realm}";
    private const string LogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    [Fact]
    public async Task Logout_token_clears_every_host_session_in_the_sso_session()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();

        // What the browser holds after signing into both apps: two host-only cookies, two
        // sessions, one Keycloak SSO session behind them.
        var adminSession = store.Seed(Session("admin", kcSessionId: "sso-1"));
        var webSession = store.Seed(Session("protofast-web", kcSessionId: "sso-1"));
        var untouched = store.Seed(Session("admin", kcSessionId: "sso-2"));

        var result = await Handle(store, key, Token(key, sid: "sso-1", audience: "admin"));

        Assert.Equal(StatusCodes.Status200OK, result.Status);
        Assert.Null(await store.GetAsync(adminSession, CancellationToken.None));
        Assert.Null(await store.GetAsync(webSession, CancellationToken.None));
        Assert.NotNull(await store.GetAsync(untouched, CancellationToken.None));
    }

    [Fact]
    public async Task Token_signed_by_another_key_is_refused()
    {
        using var realKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var session = store.Seed(Session("admin", kcSessionId: "sso-1"));

        var result = await Handle(store, realKey, Token(attackerKey, sid: "sso-1", audience: "admin"));

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
        Assert.NotNull(await store.GetAsync(session, CancellationToken.None));
    }

    [Fact]
    public async Task Id_token_replayed_as_a_logout_token_is_refused()
    {
        // An id token is signed by the same realm key and carries sid and aud. The events claim
        // and the nonce ban are the only things separating the two.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var session = store.Seed(Session("admin", kcSessionId: "sso-1"));

        var result = await Handle(store, key, Token(key, sid: "sso-1", audience: "admin", events: null));

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
        Assert.NotNull(await store.GetAsync(session, CancellationToken.None));
    }

    [Fact]
    public async Task Token_carrying_a_nonce_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var session = store.Seed(Session("admin", kcSessionId: "sso-1"));

        var result = await Handle(
            store, key, Token(key, sid: "sso-1", audience: "admin", extra: new Claim("nonce", "n-1")));

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
        Assert.NotNull(await store.GetAsync(session, CancellationToken.None));
    }

    [Fact]
    public async Task Token_for_a_client_we_do_not_serve_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var session = store.Seed(Session("admin", kcSessionId: "sso-1"));

        var result = await Handle(store, key, Token(key, sid: "sso-1", audience: "some-other-client"));

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
        Assert.NotNull(await store.GetAsync(session, CancellationToken.None));
    }

    [Fact]
    public async Task Token_without_a_sid_is_refused()
    {
        // Keycloak only omits sid when backchannel.logout.session.required is off. Sessions are
        // indexed by sid, so accepting it would report success for a logout that did nothing.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var session = store.Seed(Session("admin", kcSessionId: "sso-1"));

        var result = await Handle(store, key, Token(key, sid: null, audience: "admin"));

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
        Assert.NotNull(await store.GetAsync(session, CancellationToken.None));
    }

    [Fact]
    public async Task Expired_token_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var session = store.Seed(Session("admin", kcSessionId: "sso-1"));

        var result = await Handle(
            store, key, Token(key, sid: "sso-1", audience: "admin", expires: DateTime.UtcNow.AddMinutes(-10)));

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
        Assert.NotNull(await store.GetAsync(session, CancellationToken.None));
    }

    [Fact]
    public async Task Replayed_jti_is_accepted_once_and_ignored_after()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var guard = new FakeReplayGuard();
        var token = Token(key, sid: "sso-1", audience: "admin");

        store.Seed(Session("admin", kcSessionId: "sso-1"));
        var first = await Handle(store, key, token, guard);

        // The user signs in again — same jti replayed must not take the new session with it.
        var reSignedIn = store.Seed(Session("admin", kcSessionId: "sso-1"));
        var second = await Handle(store, key, token, guard);

        Assert.Equal(StatusCodes.Status200OK, first.Status);
        Assert.Equal(StatusCodes.Status200OK, second.Status);
        Assert.NotNull(await store.GetAsync(reSignedIn, CancellationToken.None));
    }

    [Fact]
    public async Task A_realm_we_serve_no_client_in_is_refused_before_the_keys_are_fetched()
    {
        // Selecting validation parameters means asking Keycloak for that realm's keys, and the
        // realm is read from a token nothing has verified yet. Any issuer with a /realms/ segment
        // parses, so the refusal has to land before the fetch, not on the signature check after.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var gateway = new FakeKeycloakGateway(new ECDsaSecurityKey(key));
        var session = store.Seed(Session("admin", kcSessionId: "sso-1"));

        var result = await Handle(
            store,
            key,
            Token(key, sid: "sso-1", audience: "admin", issuer: "https://auth.protofast.test/realms/made-up"),
            gateway: gateway);

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
        Assert.False(gateway.KeysFetched, "an unknown realm must not send us to Keycloak for keys");
        Assert.NotNull(await store.GetAsync(session, CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_logout_leaves_the_jti_claimable_for_the_redelivery()
    {
        // The claim outlives the token it guards. Holding one over a logout that threw would make
        // every later delivery of that token an OK that erases nothing — the sessions would then
        // survive exactly as long as if the endpoint had never been called.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var guard = new FakeReplayGuard();
        var token = Token(key, sid: "sso-1", audience: "admin");
        var session = store.Seed(Session("admin", kcSessionId: "sso-1"));

        store.FailNextDelete = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handle(store, key, token, guard));
        Assert.NotNull(await store.GetAsync(session, CancellationToken.None));

        var retry = await Handle(store, key, token, guard);

        Assert.Equal(StatusCodes.Status200OK, retry.Status);
        Assert.Null(await store.GetAsync(session, CancellationToken.None));
    }

    [Fact]
    public async Task Garbage_body_is_refused_without_touching_the_store()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new FakeSessionStore();
        var session = store.Seed(Session("admin", kcSessionId: "sso-1"));

        Assert.Equal(StatusCodes.Status400BadRequest, (await Handle(store, key, "not-a-jwt")).Status);
        Assert.Equal(StatusCodes.Status400BadRequest, (await Handle(store, key, "")).Status);
        Assert.NotNull(await store.GetAsync(session, CancellationToken.None));
    }

    private static async Task<(int Status, string Body)> Handle(
        FakeSessionStore store,
        ECDsa realmKey,
        string logoutToken,
        IReplayGuard? replayGuard = null,
        FakeKeycloakGateway? gateway = null)
    {
        var handler = new BackchannelLogout(
            gateway ?? new FakeKeycloakGateway(new ECDsaSecurityKey(realmKey)),
            store,
            new TenantResolver(Options.Create(new TenantOptions
            {
                ByHost =
                {
                    ["protofast.dev"] = new TenantConfig { Realm = Realm, ClientId = "protofast-web" },
                    ["admin.protofast.dev"] = new TenantConfig { Realm = Realm, ClientId = "admin" },
                },
            })),
            replayGuard ?? new FakeReplayGuard(),
            NullLogger<BackchannelLogout>.Instance);

        var ctx = PostForm(logoutToken);
        var result = await handler.HandleAsync(ctx, CancellationToken.None);
        await result.ExecuteAsync(ctx);

        ctx.Response.Body.Position = 0;
        return (ctx.Response.StatusCode, await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }

    private static DefaultHttpContext PostForm(string logoutToken)
    {
        var ctx = new DefaultHttpContext
        {
            // IResult.ExecuteAsync writes its body through DI-resolved JSON options and logging.
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
        };

        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes($"logout_token={Uri.EscapeDataString(logoutToken)}"));
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static string Token(
        ECDsa key,
        string? sid,
        string audience,
        string? events = LogoutEvent,
        DateTime? expires = null,
        Claim? extra = null,
        string? issuer = null)
    {
        var claims = new List<Claim>
        {
            new("sub", "user-123"),
            new("jti", "jti-fixed"),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        if (sid is not null)
        {
            claims.Add(new Claim("sid", sid));
        }

        if (events is not null)
        {
            claims.Add(new Claim("events", "{\"" + events + "\":{}}", JsonClaimValueTypes.Json));
        }

        if (extra is not null)
        {
            claims.Add(extra);
        }

        var jwt = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(new ECDsaSecurityKey(key), SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static SessionData Session(string clientId, string kcSessionId) => new()
    {
        Sub = "user-123",
        Email = "a@b.com",
        Realm = Realm,
        ClientId = clientId,
        Roles = [],
        AccessToken = "access",
        RefreshToken = "refresh",
        AccessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        RefreshExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        CreatedAt = DateTimeOffset.UtcNow,
        KcSessionId = kcSessionId,
    };

    /// <summary>Mirrors the Redis store's sid index closely enough to assert on: sessions are
    /// erased by realm + <c>sid</c>, not by their own id.</summary>
    private sealed class FakeSessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionData> _sessions = new(StringComparer.Ordinal);

        /// <summary>Fails one delete, the way a Redis blip between the claim and the erase would.</summary>
        public bool FailNextDelete { get; set; }

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
            if (FailNextDelete)
            {
                FailNextDelete = false;
                throw new InvalidOperationException("redis is having a moment");
            }

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

    private sealed class FakeReplayGuard : IReplayGuard
    {
        private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

        public Task<bool> TryClaimAsync(string key, TimeSpan window, CancellationToken ct = default) =>
            Task.FromResult(_claimed.Add(key));

        public Task ReleaseAsync(string key, CancellationToken ct = default)
        {
            _claimed.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeKeycloakGateway(SecurityKey signingKey) : IKeycloakGateway
    {
        /// <summary>Whether anyone asked for a realm's keys — in the real gateway that is a call
        /// out to Keycloak, so it is the thing an unknown realm must not reach.</summary>
        public bool KeysFetched { get; private set; }

        public string BuildAuthorizeUrl(TenantConfig tenant, string redirectUri, string state, string codeChallenge, bool registration) => "";

        public Task<KeycloakTokens> ExchangeCodeAsync(TenantConfig tenant, string code, string redirectUri, string codeVerifier, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KeycloakTokens> RefreshAsync(TenantConfig tenant, string refreshToken, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public string BuildEndSessionUrl(TenantConfig tenant, string? idTokenHint, string postLogoutRedirectUri) => "";

        public Task<TokenValidationParameters> GetValidationParametersAsync(string realm, CancellationToken ct = default)
        {
            KeysFetched = true;

            return Task.FromResult(new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            });
        }
    }
}
