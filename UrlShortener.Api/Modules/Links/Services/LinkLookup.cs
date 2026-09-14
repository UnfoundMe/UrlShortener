using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Links.Abstractions;

namespace UrlShortener.Api.Modules.Links.Services;

public class LinkLookup(AppDbContext dbContext) : ILinkLookup
{
    private readonly AppDbContext _dbContext = dbContext;

    public Task<LinkSummary?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default) =>
        _dbContext.Links
            .AsNoTracking()
            .Where(l => l.ShortCode == shortCode)
            .Select(l => new LinkSummary(l.Id, l.ShortCode, l.ClickCount))
            .FirstOrDefaultAsync(cancellationToken);
}
