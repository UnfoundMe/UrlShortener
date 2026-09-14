using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UrlShortener.Api.Modules.Links.Dtos;

namespace UrlShortener.Api.Modules.Links.Services;

// Fixed-window counter keyed by client + the current window's start time. INCR is atomic, so
// concurrent requests in the same window still count correctly; EXPIRE is only set on the first
// increment of a window so the key self-cleans without a background job. Like
// RedisLinkCacheService, a Redis outage fails open (allow the request) instead of surfacing as a
// 5xx or blocking link creation — rate limiting here is a protective measure, not a correctness
// requirement.
public class RedisIpRateLimiter(
    IConnectionMultiplexer redis,
    IOptions<RateLimitOptions> options,
    ILogger<RedisIpRateLimiter> logger) : IIpRateLimiter
{
    private readonly IConnectionMultiplexer _redis = redis;
    private readonly RateLimitOptions _options = options.Value;
    private readonly ILogger<RedisIpRateLimiter> _logger = logger;

    public async Task<RateLimitDecision> CheckAsync(string clientKey, CancellationToken cancellationToken = default)
    {
        var windowSeconds = _options.WindowSeconds;
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowStart = nowUnix - (nowUnix % windowSeconds);
        var key = $"ratelimit:shorten:{clientKey}:{windowStart}";

        try
        {
            var db = _redis.GetDatabase();
            var count = await db.StringIncrementAsync(key);
            if (count == 1)
            {
                await db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds));
            }

            if (count <= _options.PermitLimit)
            {
                return new RateLimitDecision(IsAllowed: true, RetryAfterSeconds: 0);
            }

            var retryAfterSeconds = (int)(windowStart + windowSeconds - nowUnix);
            return new RateLimitDecision(IsAllowed: false, RetryAfterSeconds: Math.Max(retryAfterSeconds, 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis rate-limit check failed for {ClientKey}; allowing the request.", clientKey);
            return new RateLimitDecision(IsAllowed: true, RetryAfterSeconds: 0);
        }
    }
}
