namespace UrlShortener.Api.Modules.Links.Dtos;

public readonly record struct RateLimitDecision(bool IsAllowed, int RetryAfterSeconds);
