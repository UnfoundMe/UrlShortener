namespace UrlShortener.Api.Modules.Analytics.Abstractions;

// The only surface the Links module is allowed to depend on in this module — a synchronous,
// non-blocking call (a bounded channel write), so the redirect hot path never awaits analytics.
public interface IClickEventRecorder
{
    void Record(long linkId, string? referrer, string? userAgent, string? ipAddress);
}
