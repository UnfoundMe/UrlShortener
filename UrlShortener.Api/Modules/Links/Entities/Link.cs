namespace UrlShortener.Api.Modules.Links.Entities;

// Cross-module references are by id only (CreatedByApiKeyId), never by navigation property,
// so this module has no compile-time dependency on the Auth or Analytics modules.
public class Link
{
    public long Id { get; set; }
    public required string ShortCode { get; set; }
    public required string OriginalUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long CreatedByApiKeyId { get; set; }
    public long ClickCount { get; set; }
}
