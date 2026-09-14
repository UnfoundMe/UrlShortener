using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Links.Entities;
using UrlShortener.Api.Modules.Links.Services;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Services;

// Task 8: LinkLookup, the Links.Abstractions.ILinkLookup implementation the Analytics module
// depends on to resolve a short code to a link's id/click count, against a real Postgres
// testcontainer.
[Collection(PostgresCollection.Name)]
public class LinkLookupTests(PostgresFixture fixture)
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

    [Fact]
    public async Task GetByShortCodeAsync_ForExistingLink_ReturnsSummary()
    {
        await using var context = await CreateContextAsync();
        var shortCode = $"lk-{Guid.NewGuid():N}"[..12];
        var link = new Link { ShortCode = shortCode, OriginalUrl = "https://example.com/lookup", CreatedByApiKeyId = 1, ClickCount = 4 };
        context.Links.Add(link);
        await context.SaveChangesAsync();

        var lookup = new LinkLookup(context);
        var summary = await lookup.GetByShortCodeAsync(shortCode);

        Assert.NotNull(summary);
        Assert.Equal(link.Id, summary!.Id);
        Assert.Equal(shortCode, summary.ShortCode);
        Assert.Equal(4, summary.ClickCount);
    }

    [Fact]
    public async Task GetByShortCodeAsync_ForUnknownShortCode_ReturnsNull()
    {
        await using var context = await CreateContextAsync();
        var lookup = new LinkLookup(context);

        var summary = await lookup.GetByShortCodeAsync("does-not-exist");

        Assert.Null(summary);
    }
}
