namespace UrlShortener.Api.Modules.Links.Abstractions;

// Another Analytics -> Links surface (alongside ILinkClickCounterUpdater): lets the analytics
// read endpoint resolve a short code to a link's id/click count without depending on the Links
// module's entities or services directly.
public interface ILinkLookup
{
    Task<LinkSummary?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);
}

public record LinkSummary(long Id, string ShortCode, long ClickCount);
