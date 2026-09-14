using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StackExchange.Redis;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Endpoints;

// End-to-end: real HTTP requests through WebApplicationFactory<Program>, against a real
// Postgres testcontainer, covering Task 5's create-then-redirect round trip.
[Collection(LinksApiCollection.Name)]
public class RedirectEndpointsTests(LinksApiFixture fixture)
{
    [Fact]
    public async Task GetCode_ForExistingLink_Returns302ToOriginalUrl()
    {
        using var authedClient = fixture.CreateClient();
        var apiKey = await fixture.CreateApiKeyAsync();
        authedClient.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        const string originalUrl = "https://example.com/redirect-target";
        var createResponse = await authedClient.PostAsJsonAsync("/links", new CreateLinkRequest(originalUrl));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();

        using var redirectClient = fixture.CreateClient(allowAutoRedirect: false);
        var response = await redirectClient.GetAsync($"/{created!.ShortCode}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(originalUrl, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task GetCode_ForUnknownCode_Returns404()
    {
        using var client = fixture.CreateClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/unknown-code");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCode_IsUnauthenticated_NoApiKeyRequired()
    {
        using var authedClient = fixture.CreateClient();
        var apiKey = await fixture.CreateApiKeyAsync();
        authedClient.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        var createResponse = await authedClient.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/public"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();

        using var anonymousClient = fixture.CreateClient(allowAutoRedirect: false);
        var response = await anonymousClient.GetAsync($"/{created!.ShortCode}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact]
    public async Task GetCode_ForExistingLink_PopulatesRedisCache()
    {
        using var authedClient = fixture.CreateClient();
        var apiKey = await fixture.CreateApiKeyAsync();
        authedClient.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        const string originalUrl = "https://example.com/cache-me";
        var createResponse = await authedClient.PostAsJsonAsync("/links", new CreateLinkRequest(originalUrl));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();

        using var redirectClient = fixture.CreateClient(allowAutoRedirect: false);
        var redirectResponse = await redirectClient.GetAsync($"/{created!.ShortCode}");
        Assert.Equal(HttpStatusCode.Found, redirectResponse.StatusCode);

        // Inspect Redis directly (via a fresh connection, independent of the app's own
        // IConnectionMultiplexer) to confirm the redirect populated the cache-aside key.
        await using var redis = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);
        var cached = await redis.GetDatabase().StringGetAsync($"link:{created.ShortCode}");

        Assert.False(cached.IsNullOrEmpty);
        var resolution = JsonSerializer.Deserialize<LinkResolution>((string)cached!);
        Assert.NotNull(resolution);
        Assert.Equal(originalUrl, resolution.OriginalUrl);
    }
}
