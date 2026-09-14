using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Modules.Links.Entities;

namespace UrlShortener.Api.Modules.Links.Services;

public class LinkService(
    AppDbContext dbContext,
    IShortCodeGenerator shortCodeGenerator,
    ILinkCacheService cache,
    ILogger<LinkService> logger) : ILinkService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IShortCodeGenerator _shortCodeGenerator = shortCodeGenerator;
    private readonly ILinkCacheService _cache = cache;
    private readonly ILogger<LinkService> _logger = logger;

    public async Task<Link> CreateAsync(string originalUrl, long createdByApiKeyId, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("OriginalUrl must be an absolute http or https URL.", nameof(originalUrl));
        }

        var link = new Link
        {
            // Placeholder to satisfy the NOT NULL + unique ShortCode column until Link.Id is
            // assigned by the identity column below — the real code is derived from that id.
            ShortCode = Guid.NewGuid().ToString("N")[..16],
            OriginalUrl = originalUrl,
            CreatedByApiKeyId = createdByApiKeyId,
        };

        _dbContext.Links.Add(link);
        await _dbContext.SaveChangesAsync(cancellationToken);

        link.ShortCode = _shortCodeGenerator.Encode(link.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return link;
    }

    public async Task<LinkResolution?> ResolveAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetAsync(shortCode, cancellationToken);
        if (cached is not null)
        {
            _logger.LogInformation("Cache hit for short code {ShortCode}.", shortCode);
            return cached;
        }

        _logger.LogInformation("Cache miss for short code {ShortCode}; falling back to Postgres.", shortCode);

        var link = await _dbContext.Links
            .AsNoTracking()
            .Where(l => l.ShortCode == shortCode)
            .Select(l => new { l.Id, l.OriginalUrl })
            .FirstOrDefaultAsync(cancellationToken);

        if (link is null)
        {
            return null;
        }

        var resolution = new LinkResolution(link.Id, link.OriginalUrl);
        await _cache.SetAsync(shortCode, resolution, cancellationToken);

        return resolution;
    }
}
