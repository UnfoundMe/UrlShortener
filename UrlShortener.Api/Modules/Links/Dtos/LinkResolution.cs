namespace UrlShortener.Api.Modules.Links.Dtos;

// What resolving a short code yields: both the redirect target and the resolved Link's id,
// since the redirect endpoint needs the id to record a click event (Task 7) and resolution
// may come from the cache just as often as Postgres.
public record LinkResolution(long LinkId, string OriginalUrl);
