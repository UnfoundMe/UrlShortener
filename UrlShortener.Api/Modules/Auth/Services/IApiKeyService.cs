using UrlShortener.Api.Modules.Auth.Entities;

namespace UrlShortener.Api.Modules.Auth.Services;

public interface IApiKeyService
{
    // Returns the raw key (shown once) and the persisted entity (hash only).
    Task<(string RawKey, ApiKey Entity)> GenerateAsync(string ownerName, string? ownerEmail, CancellationToken cancellationToken = default);

    Task<ApiKey?> ValidateAsync(string rawKey, CancellationToken cancellationToken = default);
}
