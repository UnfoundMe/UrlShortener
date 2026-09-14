using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Modules.Links.Services;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Endpoints;

// End-to-end: real HTTP requests through WebApplicationFactory<Program>, against real
// Postgres + Redis testcontainers, covering Task 4's create-link endpoint and the
// auth-handler checks Task 3 deferred to here (see docs/url-shortener-tasks.md).
[Collection(LinksApiCollection.Name)]
public class LinksEndpointsTests(LinksApiFixture fixture)
{
    [Fact]
    public async Task PostLinks_WithValidKeyAndUrl_Returns201WithRoundTrippableShortCode()
    {
        using var client = fixture.CreateClient();
        var apiKey = await fixture.CreateApiKeyAsync();
        client.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/some/page"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.NotNull(body);
        Assert.Equal("https://example.com/some/page", body!.OriginalUrl);
        Assert.False(string.IsNullOrWhiteSpace(body.ShortCode));

        var generator = new Base62ShortCodeGenerator();
        Assert.True(generator.Decode(body.ShortCode) > 0);

        Assert.Equal($"/{body.ShortCode}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PostLinks_WithoutApiKey_Returns401()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLinks_WithInvalidApiKey_Returns401()
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, "totally-invalid-key");

        var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLinks_WithMalformedUrl_Returns400()
    {
        using var client = fixture.CreateClient();
        var apiKey = await fixture.CreateApiKeyAsync();
        client.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest("not-a-url"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
