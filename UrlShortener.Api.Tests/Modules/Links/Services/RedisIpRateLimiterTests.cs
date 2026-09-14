using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UrlShortener.Api.Modules.Links.Services;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Services;

// RedisIpRateLimiter's fixed-window counting and graceful degradation when Redis is
// unreachable, against a real Redis testcontainer.
[Collection(RedisCollection.Name)]
public class RedisIpRateLimiterTests(RedisFixture fixture) : IAsyncLifetime
{
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync() => _redis = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _redis?.DisposeAsync().AsTask() ?? Task.CompletedTask;

    private RedisIpRateLimiter CreateLimiter(int permitLimit = 10, int windowSeconds = 60) =>
        new(_redis!, Options.Create(new RateLimitOptions { PermitLimit = permitLimit, WindowSeconds = windowSeconds }), NullLogger<RedisIpRateLimiter>.Instance);

    [Fact]
    public async Task CheckAsync_AllowsRequestsUpToThePermitLimit()
    {
        var limiter = CreateLimiter(permitLimit: 3);
        var clientKey = $"client-{Guid.NewGuid():N}";

        for (var i = 0; i < 3; i++)
        {
            var decision = await limiter.CheckAsync(clientKey);
            Assert.True(decision.IsAllowed);
        }
    }

    [Fact]
    public async Task CheckAsync_BlocksOnceThePermitLimitIsExceeded_AndSetsRetryAfter()
    {
        var limiter = CreateLimiter(permitLimit: 3, windowSeconds: 60);
        var clientKey = $"client-{Guid.NewGuid():N}";

        for (var i = 0; i < 3; i++)
        {
            await limiter.CheckAsync(clientKey);
        }

        var decision = await limiter.CheckAsync(clientKey);

        Assert.False(decision.IsAllowed);
        Assert.InRange(decision.RetryAfterSeconds, 1, 60);
    }

    [Fact]
    public async Task CheckAsync_TracksDifferentClientsIndependently()
    {
        var limiter = CreateLimiter(permitLimit: 1);
        var clientA = $"client-{Guid.NewGuid():N}";
        var clientB = $"client-{Guid.NewGuid():N}";

        var firstForA = await limiter.CheckAsync(clientA);
        var secondForA = await limiter.CheckAsync(clientA);
        var firstForB = await limiter.CheckAsync(clientB);

        Assert.True(firstForA.IsAllowed);
        Assert.False(secondForA.IsAllowed);
        Assert.True(firstForB.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_WhenRedisIsUnreachable_FailsOpenInsteadOfThrowing()
    {
        var deadOptions = new ConfigurationOptions
        {
            EndPoints = { "localhost:1" },
            AbortOnConnectFail = false,
            ConnectTimeout = 500,
            ConnectRetry = 1,
        };
        await using var deadRedis = await ConnectionMultiplexer.ConnectAsync(deadOptions);
        var limiter = new RedisIpRateLimiter(
            deadRedis,
            Options.Create(new RateLimitOptions { PermitLimit = 1, WindowSeconds = 60 }),
            NullLogger<RedisIpRateLimiter>.Instance);

        var decision = await limiter.CheckAsync("any-client");

        Assert.True(decision.IsAllowed);
    }
}
