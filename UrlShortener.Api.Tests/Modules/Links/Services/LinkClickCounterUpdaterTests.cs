using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Links.Entities;
using UrlShortener.Api.Modules.Links.Services;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Links.Services;

// Task 7: LinkClickCounterUpdater.IncrementClickCountAsync, the piece ClickEventWriter calls to
// bump Link.ClickCount during its batch flush, against a real Postgres testcontainer.
[Collection(PostgresCollection.Name)]
public class LinkClickCounterUpdaterTests(PostgresFixture fixture)
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

    private static async Task<Link> InsertLinkAsync(AppDbContext context, string shortCode)
    {
        var link = new Link { ShortCode = shortCode, OriginalUrl = "https://example.com", CreatedByApiKeyId = 1 };
        context.Links.Add(link);
        await context.SaveChangesAsync();

        return link;
    }

    [Fact]
    public async Task IncrementClickCountAsync_AddsIncrementByToClickCount()
    {
        await using var context = await CreateContextAsync();
        var link = await InsertLinkAsync(context, $"cnt-{Guid.NewGuid():N}"[..12]);
        var updater = new LinkClickCounterUpdater(context);

        await updater.IncrementClickCountAsync(link.Id, incrementBy: 3);

        var persisted = await context.Links.AsNoTracking().SingleAsync(l => l.Id == link.Id);
        Assert.Equal(3, persisted.ClickCount);
    }

    [Fact]
    public async Task IncrementClickCountAsync_CalledTwice_Accumulates()
    {
        await using var context = await CreateContextAsync();
        var link = await InsertLinkAsync(context, $"cnt-{Guid.NewGuid():N}"[..12]);
        var updater = new LinkClickCounterUpdater(context);

        await updater.IncrementClickCountAsync(link.Id, incrementBy: 5);
        await updater.IncrementClickCountAsync(link.Id, incrementBy: 2);

        var persisted = await context.Links.AsNoTracking().SingleAsync(l => l.Id == link.Id);
        Assert.Equal(7, persisted.ClickCount);
    }

    [Fact]
    public async Task IncrementClickCountAsync_ForUnknownLinkId_DoesNotThrow()
    {
        await using var context = await CreateContextAsync();
        var updater = new LinkClickCounterUpdater(context);

        var exception = await Record.ExceptionAsync(() => updater.IncrementClickCountAsync(linkId: -1, incrementBy: 1));

        Assert.Null(exception);
    }
}
