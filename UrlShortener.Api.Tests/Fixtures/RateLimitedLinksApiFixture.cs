using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Auth.Services;

namespace UrlShortener.Api.Tests.Fixtures;

// Boots the app with a deliberately low RateLimiting:PermitLimit against its own Postgres +
// Redis testcontainers, isolated from LinksApiFixture's shared collection so exercising the
// rate limiter here can't trip up unrelated POST /links tests (and vice versa). The host build
// itself goes through WebApplicationFactoryBuildGate — see its comment — since this fixture's
// collection can run concurrently with LinksApiCollection.
public class RateLimitedLinksApiFixture : IAsyncLifetime
{
    public const int PermitLimit = 3;
    public const int WindowSeconds = 60;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private IConnectionMultiplexer? _redisConnection;

    private WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException($"{nameof(RateLimitedLinksApiFixture)} has not been initialized.");

    public HttpClient CreateClient(bool allowAutoRedirect = true) =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = allowAutoRedirect });

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        await WebApplicationFactoryBuildGate.RunAsync(async () =>
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
                builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
                builder.UseSetting("RateLimiting:PermitLimit", PermitLimit.ToString());
                builder.UseSetting("RateLimiting:WindowSeconds", WindowSeconds.ToString());
            });

            using var scope = Factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.MigrateAsync();

            return true;
        });
    }

    public async Task<string> CreateApiKeyAsync(string ownerName = "Rate Limit Test Owner")
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var (rawKey, _) = await apiKeyService.GenerateAsync(ownerName, ownerEmail: null);

        return rawKey;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
