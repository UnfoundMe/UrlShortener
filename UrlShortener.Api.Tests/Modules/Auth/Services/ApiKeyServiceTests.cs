using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Auth.Services;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Auth.Services;

[Collection(PostgresCollection.Name)]
public class ApiKeyServiceTests(PostgresFixture fixture)
{
    private async Task<AppDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        var context = new AppDbContext(options);
        await context.Database.MigrateAsync();

        return context;
    }

    [Fact]
    public async Task ValidateAsync_WithRawKeyFromGenerate_ReturnsMatchingEntity()
    {
        await using var context = await CreateContextAsync();
        var service = new ApiKeyService(context);

        var (rawKey, entity) = await service.GenerateAsync("Jane Doe", "jane@example.com");

        var validated = await service.ValidateAsync(rawKey);

        Assert.NotNull(validated);
        Assert.Equal(entity.Id, validated.Id);
        Assert.NotNull(validated.LastUsedAt);
    }

    [Fact]
    public async Task ValidateAsync_WithWrongSecretForKnownPrefix_ReturnsNull()
    {
        await using var context = await CreateContextAsync();
        var service = new ApiKeyService(context);

        var (rawKey, _) = await service.GenerateAsync("Jane Doe", null);
        var lastChar = rawKey[^1];
        var replacement = lastChar == 'z' ? 'y' : 'z';
        var tamperedKey = rawKey[..^1] + replacement;

        var validated = await service.ValidateAsync(tamperedKey);

        Assert.Null(validated);
    }

    [Fact]
    public async Task ValidateAsync_WithCompletelyUnknownKey_ReturnsNull()
    {
        await using var context = await CreateContextAsync();
        var service = new ApiKeyService(context);

        var validated = await service.ValidateAsync("does-not-exist-at-all");

        Assert.Null(validated);
    }

    [Fact]
    public async Task ValidateAsync_ForDeactivatedKey_ReturnsNull()
    {
        await using var context = await CreateContextAsync();
        var service = new ApiKeyService(context);

        var (rawKey, entity) = await service.GenerateAsync("Jane Doe", null);
        entity.IsActive = false;
        await context.SaveChangesAsync();

        var validated = await service.ValidateAsync(rawKey);

        Assert.Null(validated);
    }

    [Fact]
    public async Task GenerateAsync_StoresOnlyAHashNotTheRawKey()
    {
        await using var context = await CreateContextAsync();
        var service = new ApiKeyService(context);

        var (rawKey, entity) = await service.GenerateAsync("Jane Doe", null);

        Assert.NotEqual(rawKey, entity.HashedKey);
        Assert.DoesNotContain(rawKey, entity.HashedKey);
    }
}
