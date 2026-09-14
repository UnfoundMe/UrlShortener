namespace UrlShortener.Api.Modules.Links.Abstractions;

// The only surface the Analytics module is allowed to depend on in this module — keeps the
// dependency direction one-way (Analytics -> Links) and stands in for what would become a
// service-to-service call if this module were ever split out of the monolith.
public interface ILinkClickCounterUpdater
{
    Task IncrementClickCountAsync(long linkId, long incrementBy, CancellationToken cancellationToken = default);
}
