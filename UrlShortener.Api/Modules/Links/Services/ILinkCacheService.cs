using UrlShortener.Api.Modules.Links.Dtos;

namespace UrlShortener.Api.Modules.Links.Services;

public interface ILinkCacheService
{
    // Returns null on a cache miss OR when Redis is unreachable — callers always fall back to Postgres.
    Task<LinkResolution?> GetAsync(string shortCode, CancellationToken cancellationToken = default);

    Task SetAsync(string shortCode, LinkResolution resolution, CancellationToken cancellationToken = default);
}
