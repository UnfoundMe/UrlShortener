using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Endpoints;

// Task 7: full create -> redirect -> click-analytics-capture round trip over real HTTP against
// real Postgres + Redis testcontainers. ClickEventWriter flushes asynchronously (batched every
// ~500ms), so these assertions poll rather than checking immediately after the redirect.
[Collection(LinksApiCollection.Name)]
public class RedirectEndpointsClickAnalyticsTests(LinksApiFixture fixture)
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
    public async Task GetCode_RecordsClickEventsAndIncrementsClickCount()
    {
        using var authedClient = fixture.CreateClient();
        var apiKey = await fixture.CreateApiKeyAsync();
        authedClient.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        var createResponse = await authedClient.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/click-analytics"));
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

        var clickEvents = await fixture.WithDbContextAsync(async dbContext =>
        {
            var linkId = await dbContext.Links.Where(l => l.ShortCode == created!.ShortCode).Select(l => l.Id).SingleAsync();
            return await dbContext.ClickEvents.AsNoTracking().Where(c => c.LinkId == linkId).ToListAsync();
        });

        Assert.Equal(3, clickEvents.Count);
        Assert.All(clickEvents, c => Assert.False(string.IsNullOrEmpty(c.UserAgent)));
        Assert.Contains(clickEvents, c => c.Referrer == "https://ref-0.example.com/");
        Assert.Contains(clickEvents, c => c.Referrer == "https://ref-1.example.com/");
        Assert.Contains(clickEvents, c => c.Referrer == "https://ref-2.example.com/");
    }

    [Fact]
    public async Task GetCode_ForUnknownCode_DoesNotRecordAClickEvent()
    {
        // The whole LinksApiCollection runs sequentially (shared fixture), so no other test's
        // click activity can be mid-flight here — a before/after total is safe.
        var countBefore = await fixture.WithDbContextAsync(dbContext => dbContext.ClickEvents.CountAsync());

        using var client = fixture.CreateClient(allowAutoRedirect: false);
        var response = await client.GetAsync("/definitely-unknown-code");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Give the writer's flush interval a fair chance to run before asserting nothing landed.
        await Task.Delay(700);

        var countAfter = await fixture.WithDbContextAsync(dbContext => dbContext.ClickEvents.CountAsync());
        Assert.Equal(countBefore, countAfter);
    }
}
