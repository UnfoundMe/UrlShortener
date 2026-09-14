using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Modules.Links.Entities;

namespace UrlShortener.Api.Modules.Links.Services;

public interface ILinkService
{
    Task<Link> CreateAsync(string originalUrl, long createdByApiKeyId, CancellationToken cancellationToken = default);

    // Resolves via cache first, falling back to Postgres on miss (Task 5 + Task 6). Returns the
    // Link's id alongside the URL so callers (the redirect endpoint) can record a click event
    // (Task 7) without a second lookup.
    Task<LinkResolution?> ResolveAsync(string shortCode, CancellationToken cancellationToken = default);
}
