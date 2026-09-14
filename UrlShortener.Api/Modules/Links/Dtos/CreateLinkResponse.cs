namespace UrlShortener.Api.Modules.Links.Dtos;

public record CreateLinkResponse(string ShortCode, string OriginalUrl, DateTimeOffset CreatedAt);
