using System.Text.Json;
using StackExchange.Redis;
using UrlShortener.Api.Modules.Links.Dtos;

namespace UrlShortener.Api.Modules.Links.Services;

// Cache-aside for redirect lookups. Every Redis call is wrapped in try/catch: a cache miss and
// a Redis outage look identical to callers (both return null / no-op), so LinkService always
// falls through to Postgres and the redirect path never surfaces a Redis failure as a 5xx.
public class RedisLinkCacheService(IConnectionMultiplexer redis, ILogger<RedisLinkCacheService> logger) : ILinkCacheService
{
    // Base TTL plus random jitter so links cached around the same time don't all expire at
    // once and stampede Postgres together.
    private static readonly TimeSpan BaseTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxJitter = TimeSpan.FromMinutes(10);

    private readonly IConnectionMultiplexer _redis = redis;
    private readonly ILogger<RedisLinkCacheService> _logger = logger;

    private static string KeyFor(string shortCode) => $"link:{shortCode}";

    public async Task<LinkResolution?> GetAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _redis.GetDatabase().StringGetAsync(KeyFor(shortCode));
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<LinkResolution>((string)value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for short code {ShortCode}; falling back to Postgres.", shortCode);
            return null;
        }
    }

    public async Task SetAsync(string shortCode, LinkResolution resolution, CancellationToken cancellationToken = default)
    {
        try
        {
            var ttl = BaseTtl + TimeSpan.FromMilliseconds(Random.Shared.Next((int)MaxJitter.TotalMilliseconds));
            await _redis.GetDatabase().StringSetAsync(KeyFor(shortCode), JsonSerializer.Serialize(resolution), ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for short code {ShortCode}; continuing without caching.", shortCode);
        }
    }
}
