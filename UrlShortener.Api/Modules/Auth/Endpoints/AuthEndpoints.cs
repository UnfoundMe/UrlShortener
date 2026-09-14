using UrlShortener.Api.Modules.Auth.Dtos;
using UrlShortener.Api.Modules.Auth.Services;

namespace UrlShortener.Api.Modules.Auth.Endpoints;

public static class AuthEndpoints
{
    // Only mapped when AuthModule.EnableSelfServeApiKeysConfigKey is true — see AuthModule.
    // Public/unauthenticated by design (that's the point of self-serve), so this must never be
    // mapped in a real deployment without abuse protection (rate limiting, email verification)
    // that doesn't exist yet.
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/api-keys", async (
            CreateApiKeyRequest request,
            IApiKeyService apiKeyService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.OwnerName))
            {
                return Results.BadRequest(new { error = "ownerName is required." });
            }

            var (rawKey, entity) = await apiKeyService.GenerateAsync(
                request.OwnerName, request.OwnerEmail, cancellationToken);

            var response = new CreateApiKeyResponse(entity.Id, entity.OwnerName, rawKey, entity.CreatedAt);

            return Results.Created($"/auth/api-keys/{entity.Id}", response);
        });

        return app;
    }
}
