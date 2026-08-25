using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Sessions;
using StackExchange.Redis;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

/// <summary>
/// The Keycloak-session index is the part of back-channel logout that can rot silently: get the
/// bookkeeping wrong and the endpoint still answers 200 while leaving sessions alive. The rest of
/// the suite is hermetic, so these skip when no Redis is listening — they run against the Aspire
/// dev Redis, and CI has none.
/// </summary>
public class RedisSessionStoreIndexTests : IAsyncLifetime
{
    // abortConnect=true on purpose: with it off, Connect succeeds against nothing and every
    // command fails later, which reads as a broken index rather than an absent server.
    private const string RedisConfiguration = "localhost:6379,abortConnect=true,connectTimeout=500,connectRetry=1";

    private readonly string _realm = $"test-{Guid.NewGuid():N}";

    private IConnectionMultiplexer? _redis;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _redis = await ConnectionMultiplexer.ConnectAsync(RedisConfiguration);
        }
        catch (RedisConnectionException)
        {
            _redis = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_erases_every_host_session_sharing_the_sso_session()
    {
        var store = Store();
        var sid = NewSid();

        // Two hosts, two cookies, two sessions — one Keycloak SSO session behind them.
        var admin = await store.CreateAsync(Session("admin", sid), Ct);
        var web = await store.CreateAsync(Session("protofast-web", sid), Ct);
        var otherBrowser = await store.CreateAsync(Session("admin", NewSid()), Ct);

        await store.DeleteByKeycloakSessionAsync(_realm, sid, Ct);

        Assert.Null(await store.GetAsync(admin, Ct));
        Assert.Null(await store.GetAsync(web, Ct));
        Assert.NotNull(await store.GetAsync(otherBrowser, Ct));
    }

    [Fact]
    public async Task Index_follows_the_session_id_through_a_refresh_rotation()
    {
        var store = Store();
        var sid = NewSid();
        var original = await store.CreateAsync(Session("admin", sid), Ct);

        var rotated = await store.ReplaceAsync(original, Session("admin", sid), Ct);
        Assert.NotEqual(original, rotated);

        await store.DeleteByKeycloakSessionAsync(_realm, sid, Ct);

        // The id the cookie now carries is the one that has to die.
        Assert.Null(await store.GetAsync(rotated, Ct));
    }

    [Fact]
    public async Task Session_without_a_keycloak_session_id_is_left_out_of_the_index()
    {
        // Sessions minted before the index existed. They keep the old behaviour — lapsing at the
        // next failed refresh — rather than being indexed under an empty sid.
        var store = Store();
        var id = await store.CreateAsync(Session("admin", kcSessionId: null), Ct);

        await store.DeleteByKeycloakSessionAsync(_realm, "", Ct);

        Assert.NotNull(await store.GetAsync(id, Ct));
    }

    [Fact]
    public async Task Index_outlives_the_idle_window()
    {
        // GetAsync slides each session's own TTL, so an index expiring on the idle window would
        // lapse under a session still in daily use and take back-channel logout with it.
        var redis = Redis();
        var options = new SessionPolicyOptions
        {
            IdleTtl = TimeSpan.FromMinutes(30),
            AbsoluteTtl = TimeSpan.FromDays(7),
        };

        var store = new RedisSessionStore(redis, Options.Create(options), TimeProvider.System);
        var sid = NewSid();
        await store.CreateAsync(Session("admin", sid), Ct);

        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync($"kcsid:{_realm}:{sid}");

        Assert.NotNull(ttl);
        Assert.True(
            ttl > options.IdleTtl,
            $"index TTL {ttl} should track the absolute cap, not the {options.IdleTtl} idle window");
    }

    private IConnectionMultiplexer Redis()
    {
        if (_redis is null)
        {
            Assert.Skip($"no Redis on {RedisConfiguration}");
        }

        return _redis;
    }

    private RedisSessionStore Store() =>
        new(Redis(), Options.Create(new SessionPolicyOptions()), TimeProvider.System);

    private static string NewSid() => $"sso-{Guid.NewGuid():N}";

    private SessionData Session(string clientId, string? kcSessionId) => new()
    {
        Sub = "user-123",
        Email = "a@b.com",
        Realm = _realm,
        ClientId = clientId,
        Roles = [],
        AccessToken = "access",
        RefreshToken = "refresh",
        AccessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        RefreshExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        CreatedAt = DateTimeOffset.UtcNow,
        KcSessionId = kcSessionId,
    };
}
