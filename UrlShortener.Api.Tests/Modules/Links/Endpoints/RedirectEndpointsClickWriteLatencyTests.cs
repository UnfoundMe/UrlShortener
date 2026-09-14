using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Auth.Services;
using UrlShortener.Api.Modules.Links.Abstractions;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Modules.Links.Services;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Endpoints;

// Task 7: proves the redirect response never waits on the click-analytics write path —
// structurally, not just "it happens to be fast". A DI-substituted ILinkClickCounterUpdater
// stalls the batch flush's DB step for several seconds; the redirect still has to return
// almost immediately, since Record() is only ever a synchronous, non-blocking channel write.
// Owns its own Postgres + Redis testcontainers (rather than the shared LinksApiFixture) because
// it needs a WebApplicationFactory with a custom service override; sits in LinksApiCollection
// anyway (without using its fixture) purely so it's serialized against every other test class
// that boots a WebApplicationFactory<Program> — Program.cs assigns a static, process-global
// Serilog Log.Logger on every boot, which throws "the logger is already frozen" if two
// factories start concurrently.
[Collection(LinksApiCollection.Name)]
public class RedirectEndpointsClickWriteLatencyTests : IAsyncLifetime
{
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(3);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();

    private WebApplicationFactory<Program>? _factory;

    // Delegates to the real LinkClickCounterUpdater (so the eventual DB write is still correct)
    // after an artificial delay, simulating a slow/blocked flush.
    private sealed class SlowLinkClickCounterUpdater(LinkClickCounterUpdater inner) : ILinkClickCounterUpdater
    {
        public async Task IncrementClickCountAsync(long linkId, long incrementBy, CancellationToken cancellationToken = default)
        {
            await Task.Delay(FlushDelay, cancellationToken);
            await inner.IncrementClickCountAsync(linkId, incrementBy, cancellationToken);
        }
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<LinkClickCounterUpdater>();
                services.AddScoped<ILinkClickCounterUpdater>(sp =>
                    new SlowLinkClickCounterUpdater(sp.GetRequiredService<LinkClickCounterUpdater>()));
            });
        });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
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

    private static async Task PollUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Condition was not met within the timeout.");
    }

    [Fact]
    public async Task GetCode_ReturnsWellBeforeTheDeliberatelySlowClickFlushCompletes()
    {
        using var setupScope = _factory!.Services.CreateScope();
        var apiKeyService = setupScope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var (rawKey, _) = await apiKeyService.GenerateAsync("Slow Flush Test", ownerEmail: null);

        using var authedClient = _factory.CreateClient();
        authedClient.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, rawKey);

        const string originalUrl = "https://example.com/slow-flush";
        var createResponse = await authedClient.PostAsJsonAsync("/links", new CreateLinkRequest(originalUrl));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();

        using var redirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var stopwatch = Stopwatch.StartNew();
        var response = await redirectClient.GetAsync($"/{created!.ShortCode}");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(originalUrl, response.Headers.Location?.ToString());

        // The flush is stalled for FlushDelay (3s) inside the writer's background loop; the
        // redirect returning in a small fraction of that proves it never awaited it.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"Redirect took {stopwatch.Elapsed}, expected it to return well before the {FlushDelay} flush delay.");

        // ...and the click eventually lands, proving it wasn't silently lost, just delayed.
        var linkId = await setupScope.ServiceProvider.GetRequiredService<AppDbContext>().Links
            .Where(l => l.ShortCode == created.ShortCode)
            .Select(l => l.Id)
            .SingleAsync();

        await PollUntilAsync(
            async () =>
            {
                using var pollScope = _factory.Services.CreateScope();
                var dbContext = pollScope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await dbContext.ClickEvents.AnyAsync(c => c.LinkId == linkId);
            },
            TimeSpan.FromSeconds(10));
    }
}
