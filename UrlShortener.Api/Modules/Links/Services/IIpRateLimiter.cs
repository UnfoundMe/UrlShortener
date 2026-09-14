using UrlShortener.Api.Modules.Links.Dtos;

namespace UrlShortener.Api.Modules.Links.Services;

public interface IIpRateLimiter
{
    Task<RateLimitDecision> CheckAsync(string clientKey, CancellationToken cancellationToken = default);
}
