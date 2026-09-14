using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Modules.Analytics.Dtos;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Analytics.Endpoints;

// Task 8: full create -> redirect -> analytics-read round trip over real HTTP against real
// Postgres + Redis testcontainers. ClickEventWriter flushes asynchronously (batched every
// ~500ms, see Task 7), so the click-reflecting assertion polls rather than checking immediately.
[Collection(LinksApiCollection.Name)]
public class AnalyticsEndpointsTests(LinksApiFixture fixture)
{
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
    public async Task GetAnalytics_WithValidKey_ReflectsGeneratedClicks()
    {
        using var authedClient = fixture.CreateClient();
        var apiKey = await fixture.CreateApiKeyAsync();
        authedClient.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        var createResponse = await authedClient.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/analytics-endpoint"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();

        using var redirectClient = fixture.CreateClient(allowAutoRedirect: false);
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{created!.ShortCode}");
            request.Headers.Referrer = new Uri($"https://ref-{i}.example.com/");
            request.Headers.UserAgent.ParseAdd($"TestAgent/{i}.0");

            var response = await redirectClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        }

        await PollUntilAsync(
            () => fixture.WithDbContextAsync(async dbContext =>
                await dbContext.Links.AsNoTracking()
                    .Where(l => l.ShortCode == created!.ShortCode)
                    .Select(l => l.ClickCount)
                    .SingleAsync() == 3),
            TimeSpan.FromSeconds(5));

        var analyticsResponse = await authedClient.GetAsync($"/links/{created!.ShortCode}/analytics");
        Assert.Equal(HttpStatusCode.OK, analyticsResponse.StatusCode);

        var analytics = await analyticsResponse.Content.ReadFromJsonAsync<LinkAnalyticsResponse>();
        Assert.NotNull(analytics);
        Assert.Equal(created.ShortCode, analytics!.ShortCode);
        Assert.Equal(3, analytics.ClickCount);
        Assert.Equal(3, analytics.RecentClicks.Count);
        Assert.Contains(analytics.RecentClicks, c => c.Referrer == "https://ref-0.example.com/" && c.UserAgent == "TestAgent/0.0");
        Assert.Contains(analytics.RecentClicks, c => c.Referrer == "https://ref-1.example.com/" && c.UserAgent == "TestAgent/1.0");
        Assert.Contains(analytics.RecentClicks, c => c.Referrer == "https://ref-2.example.com/" && c.UserAgent == "TestAgent/2.0");
    }

    [Fact]
    public async Task GetAnalytics_WithoutApiKey_Returns401()
    {
        using var authedClient = fixture.CreateClient();
        var apiKey = await fixture.CreateApiKeyAsync();
        authedClient.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        var createResponse = await authedClient.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/no-key"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();

        using var anonymousClient = fixture.CreateClient();
        var response = await anonymousClient.GetAsync($"/links/{created!.ShortCode}/analytics");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAnalytics_WithInvalidApiKey_Returns401()
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, "totally-invalid-key");

        var response = await client.GetAsync("/links/whatever-code/analytics");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAnalytics_ForUnknownCode_Returns404()
    {
        using var client = fixture.CreateClient();
        var apiKey = await fixture.CreateApiKeyAsync();
        client.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        var response = await client.GetAsync("/links/definitely-unknown-code/analytics");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
