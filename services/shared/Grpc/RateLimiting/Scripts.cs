using StackExchange.Redis;

namespace ProtoFast.Grpc.RateLimiting;

public static class Scripts
{
    // Prepared once: LuaScript.Prepare parses the script text, which there is no reason to redo
    // on every call.
    public static LuaScript SlidingWindowScript { get; } = LuaScript.Prepare(SlidingWindowLua);

    public static LuaScript FixedWindowScript { get; } = LuaScript.Prepare(FixedWindowLua);

    private const string SlidingWindowLua = @"
            local current_time = redis.call('TIME')
            local trim_time = tonumber(current_time[1]) - @window
            redis.call('ZREMRANGEBYSCORE', @key, 0, trim_time)
            local request_count = redis.call('ZCARD', @key)

            if request_count < tonumber(@max_requests) then
                redis.call('ZADD', @key, current_time[1], current_time[1] .. current_time[2])
                redis.call('EXPIRE', @key, @window)
                return {0, 0}
            end

            -- For sliding window, get the oldest request in the window
            local oldest_requests = redis.call('ZRANGE', @key, 0, 0, 'WITHSCORES')
            if #oldest_requests > 0 then
                local oldest_time = tonumber(oldest_requests[2])
                local retry_after = math.ceil(oldest_time + @window - tonumber(current_time[1]))
                return {1, retry_after}
            end
            return {1, @window}
        ";

    private const string FixedWindowLua = @"
            local current_time = redis.call('TIME')
            local window_start = math.floor(tonumber(current_time[1]) / @window) * @window
            local window_key = @key .. ':' .. tostring(window_start)

            local count = redis.call('INCR', window_key)
            if count == 1 then
                redis.call('EXPIRE', window_key, @window)
            end

            if count <= tonumber(@max_requests) then
                return {0, 0}
            end

            -- Calculate retry after: time until next window
            local next_window = window_start + @window
            local retry_after = next_window - tonumber(current_time[1])
            return {1, retry_after}
        ";
}


