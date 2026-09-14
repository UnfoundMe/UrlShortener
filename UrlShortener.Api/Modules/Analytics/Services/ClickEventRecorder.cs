using UrlShortener.Api.Modules.Analytics.Abstractions;
using UrlShortener.Api.Modules.Analytics.Channels;
using UrlShortener.Api.Modules.Analytics.Entities;

namespace UrlShortener.Api.Modules.Analytics.Services;

public class ClickEventRecorder(ClickEventChannel channel) : IClickEventRecorder
{
    private readonly ClickEventChannel _channel = channel;

    public void Record(long linkId, string? referrer, string? userAgent, string? ipAddress)
    {
        var clickEvent = new ClickEvent
        {
            LinkId = linkId,
            Referrer = referrer,
            UserAgent = userAgent,
            IpAddress = ipAddress,
        };

        // Intentionally not awaited/checked beyond TryWrite: under DropOldest this never blocks
        // and a dropped event is an accepted, disclosed loss (see docs/url-shortener-plan.md, #3).
        _channel.Writer.TryWrite(clickEvent);
    }
}
