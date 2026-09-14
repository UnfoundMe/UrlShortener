using UrlShortener.Api.Modules.Analytics.Dtos;

namespace UrlShortener.Api.Modules.Analytics.Services;

public interface IAnalyticsService
{
    // Returns null when the short code doesn't resolve to a link (endpoint maps that to 404).
    Task<LinkAnalyticsResponse?> GetAnalyticsAsync(string shortCode, int recentClicksLimit, CancellationToken cancellationToken = default);
}
