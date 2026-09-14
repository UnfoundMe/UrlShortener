using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Modules.Links.Services;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Services;

[Collection(PostgresCollection.Name)]
public class LinkServiceTests(PostgresFixture fixture)
{
    // CreateAsync must not touch the cache at all (that's ResolveAsync's job) — any call here
    // is a bug, so fail loudly instead of silently no-op-ing.
    private sealed class NeverCalledLinkCacheService : ILinkCacheService
    {
        public Task<LinkResolution?> GetAsync(string shortCode, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("LinkService.CreateAsync should not read the cache.");

        public Task SetAsync(string shortCode, LinkResolution resolution, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("LinkService.CreateAsync should not write the cache.");
    }

    // An in-memory ILinkCacheService double so ResolveAsync's cache-aside logic (check cache,
    // fall back to Postgres on miss, populate cache) can be asserted directly without a real
    // Redis testcontainer — that's RedisLinkCacheServiceTests' job.
    private sealed class FakeLinkCacheService : ILinkCacheService
    {
        private readonly Dictionary<string, LinkResolution> _store = [];

        public int GetCallCount { get; private set; }

        public bool TryGetCached(string shortCode, out LinkResolution? resolution) => _store.TryGetValue(shortCode, out resolution);

        public Task<LinkResolution?> GetAsync(string shortCode, CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult(_store.TryGetValue(shortCode, out var resolution) ? resolution : null);
        }

        public Task SetAsync(string shortCode, LinkResolution resolution, CancellationToken cancellationToken = default)
        {
            _store[shortCode] = resolution;
            return Task.CompletedTask;
        }
    }

    private async Task<AppDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        var context = new AppDbContext(options);
        await context.Database.MigrateAsync();

        return context;
    }

    private static LinkService CreateService(AppDbContext context, ILinkCacheService? cache = null) =>
        new(context, new Base62ShortCodeGenerator(), cache ?? new NeverCalledLinkCacheService(), NullLogger<LinkService>.Instance);

    [Fact]
    public async Task CreateAsync_WithValidUrl_PersistsLinkWithGeneratedShortCode()
    {
        await using var context = await CreateContextAsync();
        var service = CreateService(context);

        var link = await service.CreateAsync("https://example.com/path?query=1", createdByApiKeyId: 42);

        Assert.True(link.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(link.ShortCode));
        Assert.Equal("https://example.com/path?query=1", link.OriginalUrl);
        Assert.Equal(42, link.CreatedByApiKeyId);

        var generator = new Base62ShortCodeGenerator();
        Assert.Equal(link.Id, generator.Decode(link.ShortCode));

        var persisted = await context.Links.SingleAsync(l => l.Id == link.Id);
        Assert.Equal(link.ShortCode, persisted.ShortCode);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("ftp://example.com/file")]
    [InlineData("/relative/path")]
    public async Task CreateAsync_WithMalformedOrNonHttpUrl_ThrowsArgumentException(string originalUrl)
    {
        await using var context = await CreateContextAsync();
        var service = CreateService(context);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(originalUrl, createdByApiKeyId: 1));
    }

    [Fact]
    public async Task CreateAsync_CalledTwice_ProducesDistinctShortCodes()
    {
        await using var context = await CreateContextAsync();
        var service = CreateService(context);

        var first = await service.CreateAsync("https://example.com/a", createdByApiKeyId: 1);
        var second = await service.CreateAsync("https://example.com/b", createdByApiKeyId: 1);

        Assert.NotEqual(first.ShortCode, second.ShortCode);
    }

    [Fact]
    public async Task ResolveAsync_ForExistingShortCode_ReturnsOriginalUrlAndLinkId()
    {
        await using var context = await CreateContextAsync();
        var createService = CreateService(context);
        var link = await createService.CreateAsync("https://example.com/resolve-me", createdByApiKeyId: 7);

        var service = CreateService(context, new FakeLinkCacheService());
        var resolved = await service.ResolveAsync(link.ShortCode);

        Assert.NotNull(resolved);
        Assert.Equal(link.Id, resolved.LinkId);
        Assert.Equal("https://example.com/resolve-me", resolved.OriginalUrl);
    }

    [Fact]
    public async Task ResolveAsync_ForUnknownShortCode_ReturnsNull()
    {
        await using var context = await CreateContextAsync();
        var service = CreateService(context, new FakeLinkCacheService());

        var resolved = await service.ResolveAsync("does-not-exist");

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveAsync_OnCacheMiss_PopulatesCacheFromPostgres()
    {
        await using var context = await CreateContextAsync();
        var createService = CreateService(context);
        var link = await createService.CreateAsync("https://example.com/populate-cache", createdByApiKeyId: 3);

        var cache = new FakeLinkCacheService();
        var service = CreateService(context, cache);

        await service.ResolveAsync(link.ShortCode);

        Assert.True(cache.TryGetCached(link.ShortCode, out var cached));
        Assert.Equal(link.Id, cached!.LinkId);
        Assert.Equal("https://example.com/populate-cache", cached.OriginalUrl);
    }

    [Fact]
    public async Task ResolveAsync_OnCacheHit_ReturnsCachedValueWithoutQueryingPostgres()
    {
        await using var context = await CreateContextAsync();
        var cache = new FakeLinkCacheService();
        await cache.SetAsync("cached-code", new LinkResolution(99, "https://example.com/from-cache"));
        var service = CreateService(context, cache);

        var resolved = await service.ResolveAsync("cached-code");

        Assert.Equal(99, resolved!.LinkId);
        Assert.Equal("https://example.com/from-cache", resolved.OriginalUrl);
        Assert.Equal(1, cache.GetCallCount);
    }
}
