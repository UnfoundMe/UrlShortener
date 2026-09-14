using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Auth.Services;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Endpoints;

// Task 6: the redirect endpoint must degrade gracefully when Redis is unreachable — no 5xx,
// just a Postgres-served redirect. Boots its own WebApplicationFactory with the Redis
// connection string pointed at a dead endpoint (rather than stopping the shared
// LinksApiFixture's container, which other test classes in that collection also rely on) and
// its own Postgres container (rather than the shared PostgresFixture) so it can sit in
// LinksApiCollection alongside every other test class that boots a WebApplicationFactory<Program>
// — Program.cs assigns a static, process-global Serilog Log.Logger on every boot, which throws
// "the logger is already frozen" if two factories start concurrently, so all such classes must
// be serialized against each other via one shared xUnit collection.
[Collection(LinksApiCollection.Name)]
public class RedirectEndpointsRedisUnavailableTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Redis", "localhost:1,abortConnect=false,connectTimeout=500,connectRetry=1");
        });

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task GetCode_WithRedisUnreachable_StillReturns302ViaPostgresFallback()
    {
        using var scope = _factory!.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var (rawKey, _) = await apiKeyService.GenerateAsync("Redis Degraded Test", ownerEmail: null);

        using var authedClient = _factory.CreateClient();
        authedClient.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, rawKey);

        const string originalUrl = "https://example.com/redis-down";
        var createResponse = await authedClient.PostAsJsonAsync("/links", new CreateLinkRequest(originalUrl));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();

        using var redirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await redirectClient.GetAsync($"/{created!.ShortCode}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(originalUrl, response.Headers.Location?.ToString());
    }
}
