using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Endpoints;

// Exercises the IP-based rate limiter on POST /links end-to-end, against an isolated fixture
// configured with a low PermitLimit (RateLimitedLinksApiFixture.PermitLimit) so it doesn't
// interfere with — or get tripped up by — the many other POST /links calls in LinksApiCollection.
[Collection(RateLimitedLinksApiCollection.Name)]
public class LinksRateLimitingTests(RateLimitedLinksApiFixture fixture) : IAsyncLifetime
{
    // The fixture (and its Redis-backed rate limiter) is shared across every test in this class,
    // so without a reset each test's quota would carry over from whichever test ran before it.
    public Task InitializeAsync() => fixture.ResetRateLimitStateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> CreateAuthedClientAsync()
    {
        var client = fixture.CreateClient();
        var apiKey = await fixture.CreateApiKeyAsync();
        client.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        return client;
    }

    [Fact]
    public async Task PostLinks_WithinThePermitLimit_Returns201()
    {
        using var client = await CreateAuthedClientAsync();

        for (var i = 0; i < RateLimitedLinksApiFixture.PermitLimit; i++)
        {
            var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest($"https://example.com/within-limit-{i}"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    [Fact]
    public async Task PostLinks_BeyondThePermitLimit_Returns429WithRetryAfterHeader()
    {
        using var client = await CreateAuthedClientAsync();

        for (var i = 0; i < RateLimitedLinksApiFixture.PermitLimit; i++)
        {
            var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest($"https://example.com/over-limit-{i}"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var throttled = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/over-limit-final"));

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.True(throttled.Headers.RetryAfter is not null || throttled.Headers.Contains("Retry-After"));

        var retryAfterSeconds = int.Parse(throttled.Headers.GetValues("Retry-After").Single());
        Assert.InRange(retryAfterSeconds, 1, RateLimitedLinksApiFixture.WindowSeconds);
    }

    [Fact]
    public async Task GetCode_IsNeverRateLimited_EvenAfterExceedingThePostLimit()
    {
        using var client = await CreateAuthedClientAsync();

        CreateLinkResponse? created = null;
        for (var i = 0; i < RateLimitedLinksApiFixture.PermitLimit; i++)
        {
            var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest($"https://example.com/redirect-unaffected-{i}"));
            created = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        }

        // Exceed the POST limit.
        await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/redirect-unaffected-final"));

        using var redirectClient = fixture.CreateClient(allowAutoRedirect: false);

        for (var i = 0; i < RateLimitedLinksApiFixture.PermitLimit + 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{created!.ShortCode}");
            var response = await redirectClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        }
    }
}
