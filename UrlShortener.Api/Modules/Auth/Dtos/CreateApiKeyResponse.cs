namespace UrlShortener.Api.Modules.Auth.Dtos;

public record CreateApiKeyResponse(long Id, string OwnerName, string ApiKey, DateTimeOffset CreatedAt);
