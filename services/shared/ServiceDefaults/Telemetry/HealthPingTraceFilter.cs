using System.Diagnostics;
using OpenTelemetry;

namespace ProtoFast.ServiceDefaults.Telemetry;

/// <summary>
/// Drops the backing-service "ping" spans that the Aspire integrations
/// (<c>AddNpgsqlDataSource</c>/<c>AddRedisClient</c>) emit for health probes and
/// keep-alive traffic. These fire on every dashboard/grpc_health_probe poll and,
/// because the parent <c>/health</c> request span is already filtered out, they
/// would otherwise be exported as noisy orphan root traces.
///
/// Filtering here (rather than via each integration's own options) keeps the rule
/// in one place and works for Redis, whose instrumentation has no per-command
/// filter. Clearing the <see cref="ActivityTraceFlags.Recorded"/> flag in
/// <c>OnEnd</c> is the documented way to drop an activity; the exporter's batch
/// processor no-ops on unrecorded activities, so this must run BEFORE it.
/// </summary>
internal sealed class HealthPingTraceFilter : BaseProcessor<Activity>
{
    private const string NpgsqlSourceName = "Npgsql";
    private const string RedisSourceName = "OpenTelemetry.Instrumentation.StackExchangeRedis";

    public override void OnEnd(Activity activity)
    {
        if (ShouldDrop(activity))
        {
            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }

    private static bool ShouldDrop(Activity activity) => activity.Source.Name switch
    {
        // StackExchange.Redis names the span after the command. PING is both the
        // health-check probe and the multiplexer's keep-alive heartbeat.
        RedisSourceName => string.Equals(activity.DisplayName, "PING", StringComparison.OrdinalIgnoreCase),
        // Aspire's Npgsql health check runs "SELECT 1" on every poll.
        NpgsqlSourceName => IsSelectOne(activity.GetTagItem("db.query.text") as string),
        _ => false,
    };

    private static bool IsSelectOne(string? sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return false;
        }

        var trimmed = sql.Trim().TrimEnd(';').TrimEnd();
        return string.Equals(trimmed, "SELECT 1", StringComparison.OrdinalIgnoreCase);
    }
}
