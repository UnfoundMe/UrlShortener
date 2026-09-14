namespace UrlShortener.Api.Modules.Analytics.Entities;

// LinkId is a scalar FK only — no navigation property to Links.Entities.Link, so this
// module has no compile-time dependency on the Links module's internals.
public class ClickEvent
{
    public long Id { get; set; }
    public long LinkId { get; set; }
    public DateTimeOffset ClickedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
}
