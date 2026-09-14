namespace UrlShortener.Api.Modules.Analytics.Dtos;

public record LinkAnalyticsResponse(string ShortCode, long ClickCount, IReadOnlyList<ClickEventSummary> RecentClicks);

public record ClickEventSummary(DateTimeOffset ClickedAt, string? Referrer, string? UserAgent);
