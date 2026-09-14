namespace UrlShortener.Api.Modules.Auth.Dtos;

public record CreateApiKeyRequest(string OwnerName, string? OwnerEmail);
