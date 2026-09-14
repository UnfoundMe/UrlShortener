using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Analytics.Dtos;
using UrlShortener.Api.Modules.Links.Abstractions;

namespace UrlShortener.Api.Modules.Analytics.Services;

// ClickCount + the link's identity come from the Links module via ILinkLookup (the only surface
// this module is allowed to depend on there); recent ClickEvents are queried directly since this
// module owns that entity.
public class AnalyticsService(AppDbContext dbContext, ILinkLookup linkLookup) : IAnalyticsService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly ILinkLookup _linkLookup = linkLookup;

    public async Task<LinkAnalyticsResponse?> GetAnalyticsAsync(string shortCode, int recentClicksLimit, CancellationToken cancellationToken = default)
    {
        var link = await _linkLookup.GetByShortCodeAsync(shortCode, cancellationToken);
        if (link is null)
        {
            return null;
        }

        var recentClicks = await _dbContext.ClickEvents
            .AsNoTracking()
            .Where(c => c.LinkId == link.Id)
            .OrderByDescending(c => c.ClickedAt)
            .Take(recentClicksLimit)
            .Select(c => new ClickEventSummary(c.ClickedAt, c.Referrer, c.UserAgent))
            .ToListAsync(cancellationToken);

        return new LinkAnalyticsResponse(link.ShortCode, link.ClickCount, recentClicks);
    }
}
