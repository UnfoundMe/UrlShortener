using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Analytics.Entities;
using UrlShortener.Api.Modules.Analytics.Services;
using UrlShortener.Api.Modules.Links.Entities;
using UrlShortener.Api.Modules.Links.Services;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Analytics.Services;

// Task 8: AnalyticsService against a real Postgres testcontainer — combines Links.LinkLookup
// (the cross-module abstraction) with this module's own ClickEvents to build the response the
// GET /links/{code}/analytics endpoint returns.
[Collection(PostgresCollection.Name)]
public class AnalyticsServiceTests(PostgresFixture fixture)
{
    private async Task<AppDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        var context = new AppDbContext(options);
        await context.Database.MigrateAsync();

        return context;
    }

    private static AnalyticsService CreateService(AppDbContext context) => new(context, new LinkLookup(context));

    private static async Task<Link> InsertLinkAsync(AppDbContext context, string shortCode, long clickCount = 0)
    {
        var link = new Link { ShortCode = shortCode, OriginalUrl = "https://example.com", CreatedByApiKeyId = 1, ClickCount = clickCount };
        context.Links.Add(link);
        await context.SaveChangesAsync();

        return link;
    }

    private static async Task InsertClickEventAsync(AppDbContext context, long linkId, DateTimeOffset clickedAt, string referrer, string userAgent)
    {
        context.ClickEvents.Add(new ClickEvent
        {
            LinkId = linkId,
            ClickedAt = clickedAt,
            Referrer = referrer,
            UserAgent = userAgent,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAnalyticsAsync_ForExistingLink_ReturnsClickCountAndRecentClicks()
    {
        await using var context = await CreateContextAsync();
        var link = await InsertLinkAsync(context, $"an-{Guid.NewGuid():N}"[..12], clickCount: 2);
        var now = DateTimeOffset.UtcNow;
        await InsertClickEventAsync(context, link.Id, now.AddMinutes(-1), "https://ref-a.example.com/", "AgentA/1.0");
        await InsertClickEventAsync(context, link.Id, now, "https://ref-b.example.com/", "AgentB/1.0");

        var service = CreateService(context);
        var analytics = await service.GetAnalyticsAsync(link.ShortCode, recentClicksLimit: 20);

        Assert.NotNull(analytics);
        Assert.Equal(link.ShortCode, analytics!.ShortCode);
        Assert.Equal(2, analytics.ClickCount);
        Assert.Equal(2, analytics.RecentClicks.Count);

        // Most recent first.
        Assert.Equal("https://ref-b.example.com/", analytics.RecentClicks[0].Referrer);
        Assert.Equal("AgentB/1.0", analytics.RecentClicks[0].UserAgent);
        Assert.Equal("https://ref-a.example.com/", analytics.RecentClicks[1].Referrer);
    }

    [Fact]
    public async Task GetAnalyticsAsync_WithMoreClicksThanLimit_ReturnsOnlyTheMostRecentUpToLimit()
    {
        await using var context = await CreateContextAsync();
        var link = await InsertLinkAsync(context, $"an-{Guid.NewGuid():N}"[..12], clickCount: 5);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            await InsertClickEventAsync(context, link.Id, now.AddMinutes(-i), $"https://ref-{i}.example.com/", $"Agent{i}/1.0");
        }

        var service = CreateService(context);
        var analytics = await service.GetAnalyticsAsync(link.ShortCode, recentClicksLimit: 2);

        Assert.NotNull(analytics);
        Assert.Equal(5, analytics!.ClickCount);
        Assert.Equal(2, analytics.RecentClicks.Count);
        Assert.Equal("https://ref-0.example.com/", analytics.RecentClicks[0].Referrer);
        Assert.Equal("https://ref-1.example.com/", analytics.RecentClicks[1].Referrer);
    }

    [Fact]
    public async Task GetAnalyticsAsync_ForLinkWithNoClicks_ReturnsZeroCountAndEmptyRecentClicks()
    {
        await using var context = await CreateContextAsync();
        var link = await InsertLinkAsync(context, $"an-{Guid.NewGuid():N}"[..12]);

        var service = CreateService(context);
        var analytics = await service.GetAnalyticsAsync(link.ShortCode, recentClicksLimit: 20);

        Assert.NotNull(analytics);
        Assert.Equal(0, analytics!.ClickCount);
        Assert.Empty(analytics.RecentClicks);
    }

    [Fact]
    public async Task GetAnalyticsAsync_ForUnknownShortCode_ReturnsNull()
    {
        await using var context = await CreateContextAsync();
        var service = CreateService(context);

        var analytics = await service.GetAnalyticsAsync("does-not-exist", recentClicksLimit: 20);

        Assert.Null(analytics);
    }
}
