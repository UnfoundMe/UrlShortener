using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Auth.Services;

namespace UrlShortener.Api.Tests.Fixtures;

// Boots the full app (Program.cs) against real Postgres + Redis testcontainers, for
// endpoint-level tests driven over real HTTP via WebApplicationFactory<Program>.
public class LinksApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException($"{nameof(LinksApiFixture)} has not been initialized.");

    // Exposed so tests can open their own connection to inspect cache state directly
    // (StringGet) without going through the app's own IConnectionMultiplexer.
    public string RedisConnectionString => _redis.GetConnectionString();

    // Task 9 (docs/url-shortener-tasks.md): captures every log event the app emits (across the
    // whole shared fixture/collection) so tests can assert specific observability events fired.
    public CapturingLoggerProvider LogEntries { get; } = new();

    // Polls Entries until a matching one shows up or the timeout elapses, rather than requiring
    // a fixed delay — log events (e.g. from the async ClickEventWriter) don't land synchronously.
    public async Task<CapturedLogEntry> WaitForLogAsync(
        Func<CapturedLogEntry, bool> predicate,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        while (DateTime.UtcNow < deadline)
        {
            var match = LogEntries.Entries.FirstOrDefault(e => predicate(e));
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Timed out waiting for a matching log entry.");
    }

    // allowAutoRedirect: false so redirect-endpoint tests can inspect the 302/Location header
    // directly instead of the client transparently following it out to the real target URL.
    public HttpClient CreateClient(bool allowAutoRedirect = true) =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = allowAutoRedirect });

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
            builder.ConfigureLogging(logging => logging.AddProvider(LogEntries));
        });

        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    // Mints a real, usable API key the same way the "seed-api-key" CLI bootstrap does, so
    // endpoint tests can authenticate without a throwaway/self-serve signup path.
    public async Task<string> CreateApiKeyAsync(string ownerName = "Test Owner")
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var (rawKey, _) = await apiKeyService.GenerateAsync(ownerName, ownerEmail: null);

        return rawKey;
    }

    // Gives tests a scoped AppDbContext against the same Postgres the app itself uses, e.g. to
    // poll for rows a background service (ClickEventWriter) writes asynchronously.
    public async Task<T> WithDbContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
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
