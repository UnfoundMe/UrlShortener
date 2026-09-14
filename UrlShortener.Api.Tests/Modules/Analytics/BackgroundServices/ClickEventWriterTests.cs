using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Analytics.BackgroundServices;
using UrlShortener.Api.Modules.Analytics.Channels;
using UrlShortener.Api.Modules.Analytics.Entities;
using UrlShortener.Api.Modules.Links.Abstractions;
using UrlShortener.Api.Modules.Links.Entities;
using UrlShortener.Api.Modules.Links.Services;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Analytics.BackgroundServices;

// Task 7: ClickEventWriter, run as a real hosted BackgroundService (StartAsync/StopAsync, not
// its internals called directly), draining a real ClickEventChannel and flushing ClickEvent
// rows + aggregated Link.ClickCount updates to a real Postgres testcontainer.
[Collection(PostgresCollection.Name)]
public class ClickEventWriterTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ServiceProvider? _services;
    private ClickEventChannel? _channel;
    private ClickEventWriter? _writer;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddScoped<ILinkClickCounterUpdater, LinkClickCounterUpdater>();
        services.AddSingleton<ClickEventChannel>();

        _services = services.BuildServiceProvider();
        _channel = _services.GetRequiredService<ClickEventChannel>();

        await WithScopeAsync(dbContext => dbContext.Database.MigrateAsync());

        _writer = new ClickEventWriter(_channel, _services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ClickEventWriter>.Instance);
        await _writer.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_writer is not null)
        {
            await _writer.StopAsync(CancellationToken.None);
            _writer.Dispose();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
    }

    private async Task<T> WithScopeAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _services!.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task WithScopeAsync(Func<AppDbContext, Task> action) =>
        WithScopeAsync(async dbContext =>
        {
            await action(dbContext);
            return true;
        });

    private Task<Link> InsertLinkAsync(string shortCode) =>
        WithScopeAsync(async dbContext =>
        {
            var link = new Link { ShortCode = shortCode, OriginalUrl = "https://example.com", CreatedByApiKeyId = 1 };
            dbContext.Links.Add(link);
            await dbContext.SaveChangesAsync();

            return link;
        });

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
    public async Task Writer_FlushesFewerEventsThanBatchSize_WithinTheFlushInterval()
    {
        // Well under the writer's batch size (100), so this can only land via the ~500ms
        // time-based flush, not the size threshold.
        var linkA = await InsertLinkAsync($"a-{Guid.NewGuid():N}"[..10]);
        var linkB = await InsertLinkAsync($"b-{Guid.NewGuid():N}"[..10]);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(_channel!.Writer.TryWrite(new ClickEvent { LinkId = linkA.Id, Referrer = $"ref-a-{i}" }));
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.True(_channel!.Writer.TryWrite(new ClickEvent { LinkId = linkB.Id, Referrer = $"ref-b-{i}" }));
        }

        await PollUntilAsync(
            () => WithScopeAsync(async dbContext =>
                await dbContext.ClickEvents.CountAsync(c => c.LinkId == linkA.Id || c.LinkId == linkB.Id) == 8),
            TimeSpan.FromSeconds(5));

        var updatedLinkA = await WithScopeAsync(dbContext => dbContext.Links.AsNoTracking().SingleAsync(l => l.Id == linkA.Id));
        var updatedLinkB = await WithScopeAsync(dbContext => dbContext.Links.AsNoTracking().SingleAsync(l => l.Id == linkB.Id));

        Assert.Equal(5, updatedLinkA.ClickCount);
        Assert.Equal(3, updatedLinkB.ClickCount);
    }

    [Fact]
    public async Task Writer_PersistsClickEventFieldsVerbatim()
    {
        var link = await InsertLinkAsync($"f-{Guid.NewGuid():N}"[..10]);

        _channel!.Writer.TryWrite(new ClickEvent
        {
            LinkId = link.Id,
            Referrer = "https://example.com/from",
            UserAgent = "TestAgent/1.0",
            IpAddress = "203.0.113.5",
        });

        await PollUntilAsync(
            () => WithScopeAsync(dbContext => dbContext.ClickEvents.AnyAsync(c => c.LinkId == link.Id)),
            TimeSpan.FromSeconds(5));

        var clickEvent = await WithScopeAsync(dbContext => dbContext.ClickEvents.AsNoTracking().SingleAsync(c => c.LinkId == link.Id));

        Assert.Equal("https://example.com/from", clickEvent.Referrer);
        Assert.Equal("TestAgent/1.0", clickEvent.UserAgent);
        Assert.Equal("203.0.113.5", clickEvent.IpAddress);
    }

    [Fact]
    public async Task Writer_FlushesAcrossMultipleBatches_WhenEventCountExceedsBatchSize()
    {
        // 250 events for one link: the 100-event batch size means this needs multiple flush
        // cycles to fully drain, exercising both the size- and time-based flush triggers.
        var link = await InsertLinkAsync($"m-{Guid.NewGuid():N}"[..10]);

        for (var i = 0; i < 250; i++)
        {
            Assert.True(_channel!.Writer.TryWrite(new ClickEvent { LinkId = link.Id }));
        }

        await PollUntilAsync(
            () => WithScopeAsync(async dbContext => await dbContext.ClickEvents.CountAsync(c => c.LinkId == link.Id) == 250),
            TimeSpan.FromSeconds(10));

        var updatedLink = await WithScopeAsync(dbContext => dbContext.Links.AsNoTracking().SingleAsync(l => l.Id == link.Id));
        Assert.Equal(250, updatedLink.ClickCount);
    }
}
