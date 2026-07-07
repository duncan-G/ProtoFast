using System.Text.Json;
using StackExchange.Redis;

namespace ProtoFast.Auth.Api.Correlation;

public sealed class RedisCorrelationStore(IConnectionMultiplexer redis) : ICorrelationStore
{
    private const string KeyPrefix = "corr:";

    // An in-flight authorize round-trip is short; 10 min covers a slow login without lingering.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _db = redis.GetDatabase();

    public Task SaveAsync(string state, CorrelationData data, CancellationToken ct = default) =>
        _db.StringSetAsync(KeyPrefix + state, JsonSerializer.Serialize(data, JsonOptions), Ttl);

    public async Task<CorrelationData?> TakeAsync(string state, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(state))
        {
            return null;
        }

        // GETDEL — atomic single-use read so a replayed state can't be reused.
        var json = await _db.StringGetDeleteAsync(KeyPrefix + state);
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CorrelationData>(json.ToString(), JsonOptions);
    }
}
