using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Auth;
using UrlShortener.Api.Modules.Auth.Dtos;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Auth.Endpoints;

// Boots its own WebApplicationFactory with the self-serve flag explicitly forced off (rather
// than relying on the shared LinksApiFixture's Development-default "on"), and its own Postgres
// container, so it can sit in LinksApiCollection alongside every other WebApplicationFactory<Program>
// test class — see RedirectEndpointsRedisUnavailableTests for why that grouping is required
// (Program.cs's process-global Serilog Log.Logger throws if two factories boot concurrently).
[Collection(LinksApiCollection.Name)]
public class AuthEndpointsDisabledTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            // Nothing in this test touches Redis — point at a dead endpoint (same trick as
            // RedirectEndpointsRedisUnavailableTests) instead of spinning up a real container.
            builder.UseSetting("ConnectionStrings:Redis", "localhost:1,abortConnect=false,connectTimeout=500,connectRetry=1");
            builder.UseSetting(AuthModule.EnableSelfServeApiKeysConfigKey, "false");
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
    public async Task PostAuthApiKeys_WithFlagDisabled_Returns404()
    {
        using var client = _factory!.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/api-keys", new CreateApiKeyRequest("Should Not Work", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
