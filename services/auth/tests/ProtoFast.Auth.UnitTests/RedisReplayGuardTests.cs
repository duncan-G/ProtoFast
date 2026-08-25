using ProtoFast.Auth.Api.Security;
using StackExchange.Redis;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

/// <summary>
/// The guard is two Redis commands, and both of them are the kind that look right and behave
/// wrong against a real server — SET NX has to refuse the second caller, and the release has to
/// leave the key genuinely claimable again rather than merely absent from a local set. Skips when
/// no Redis is listening, like <see cref="RedisSessionStoreIndexTests"/>.
/// </summary>
public class RedisReplayGuardTests : IAsyncLifetime
{
    private const string RedisConfiguration = "localhost:6379,abortConnect=true,connectTimeout=500,connectRetry=1";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly string _key = $"test-{Guid.NewGuid():N}";

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
    public async Task First_caller_claims_the_key_and_the_next_is_refused()
    {
        var guard = Guard();

        Assert.True(await guard.TryClaimAsync(_key, Window, Ct));
        Assert.False(await guard.TryClaimAsync(_key, Window, Ct));
    }

    [Fact]
    public async Task A_released_claim_can_be_taken_again()
    {
        // The back-channel logout endpoint releases when the erase it guarded threw. If the key
        // did not come back, a redelivery of that logout token would read as a replay and answer
        // OK while erasing nothing.
        var guard = Guard();

        Assert.True(await guard.TryClaimAsync(_key, Window, Ct));
        await guard.ReleaseAsync(_key, Ct);

        Assert.True(await guard.TryClaimAsync(_key, Window, Ct));
    }

    [Fact]
    public async Task Releasing_a_key_nobody_holds_is_not_an_error()
    {
        // The release runs on a failure path, where the claim may never have been taken.
        await Guard().ReleaseAsync(_key, Ct);
    }

    [Fact]
    public async Task A_claim_expires_with_its_window()
    {
        var guard = Guard();

        Assert.True(await guard.TryClaimAsync(_key, TimeSpan.FromSeconds(30), Ct));

        var ttl = await _redis!.GetDatabase().KeyTimeToLiveAsync($"replay:{_key}");

        Assert.NotNull(ttl);
        Assert.True(ttl <= TimeSpan.FromSeconds(30), $"claim TTL {ttl} should not outlive its window");
    }

    private RedisReplayGuard Guard()
    {
        if (_redis is null)
        {
            Assert.Skip($"no Redis on {RedisConfiguration}");
        }

        return new RedisReplayGuard(_redis);
    }
}
