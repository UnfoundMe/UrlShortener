using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Links.Abstractions;

namespace UrlShortener.Api.Modules.Links.Services;

// Bumps Link.ClickCount, called from the Analytics module's batch flush — kept as its own
// small class rather than folded into LinkService so the Analytics-facing contract
// (ILinkClickCounterUpdater) stays narrow.
public class LinkClickCounterUpdater(AppDbContext dbContext) : ILinkClickCounterUpdater
{
    private readonly AppDbContext _dbContext = dbContext;

    // ExecuteUpdateAsync issues a single UPDATE ... SET "ClickCount" = "ClickCount" + @p, with
    // no round trip to load the entity first, and participates in the caller's ambient
    // transaction (the batch flush's ClickEvent insert) when one is active on this DbContext.
    public Task IncrementClickCountAsync(long linkId, long incrementBy, CancellationToken cancellationToken = default) =>
        _dbContext.Links
            .Where(l => l.Id == linkId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.ClickCount, l => l.ClickCount + incrementBy), cancellationToken);
}
