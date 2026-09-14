using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Modules.Links.Services;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Services;

// Task 6: RedisLinkCacheService's get/set/TTL behavior, plus graceful degradation when Redis
// is unreachable, against a real Redis testcontainer.
[Collection(RedisCollection.Name)]
public class RedisLinkCacheServiceTests(RedisFixture fixture) : IAsyncLifetime
{
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync() => _redis = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _redis?.DisposeAsync().AsTask() ?? Task.CompletedTask;

    private RedisLinkCacheService CreateService() => new(_redis!, NullLogger<RedisLinkCacheService>.Instance);

    [Fact]
    public async Task SetThenGet_RoundTripsResolution()
    {
        var service = CreateService();
        var shortCode = $"code-{Guid.NewGuid():N}";

        await service.SetAsync(shortCode, new LinkResolution(42, "https://example.com/cached"));
        var result = await service.GetAsync(shortCode);

        Assert.NotNull(result);
        Assert.Equal(42, result.LinkId);
        Assert.Equal("https://example.com/cached", result.OriginalUrl);
    }

    [Fact]
    public async Task Get_ForUncachedShortCode_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetAsync($"missing-{Guid.NewGuid():N}");

        Assert.Null(result);
    }

    [Fact]
    public async Task Set_AppliesATtlSoTheKeyIsNotPermanent()
    {
        var service = CreateService();
        var shortCode = $"code-{Guid.NewGuid():N}";

        await service.SetAsync(shortCode, new LinkResolution(1, "https://example.com/ttl"));

        var ttl = await _redis!.GetDatabase().KeyTimeToLiveAsync($"link:{shortCode}");

        Assert.NotNull(ttl);
        Assert.True(ttl > TimeSpan.Zero && ttl <= TimeSpan.FromHours(24) + TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task GetAndSet_WhenRedisIsUnreachable_DegradeGracefullyInsteadOfThrowing()
    {
        // AbortOnConnectFail: false so ConnectAsync returns a (disconnected) multiplexer
        // immediately instead of throwing, matching what happens in production when Redis
        // is down at app startup or drops mid-run.
        var deadOptions = new ConfigurationOptions
        {
            EndPoints = { "localhost:1" },
            AbortOnConnectFail = false,
            ConnectTimeout = 500,
            ConnectRetry = 1,
        };
        await using var deadRedis = await ConnectionMultiplexer.ConnectAsync(deadOptions);
        var service = new RedisLinkCacheService(deadRedis, NullLogger<RedisLinkCacheService>.Instance);

        var getResult = await service.GetAsync("any-code");
        var setException = await Record.ExceptionAsync(() => service.SetAsync("any-code", new LinkResolution(1, "https://example.com/unused")));

        Assert.Null(getResult);
        Assert.Null(setException);
    }
}
