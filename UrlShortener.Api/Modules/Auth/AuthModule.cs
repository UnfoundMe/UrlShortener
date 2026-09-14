using Microsoft.AspNetCore.Authentication;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Auth.Endpoints;
using UrlShortener.Api.Modules.Auth.Handlers;
using UrlShortener.Api.Modules.Auth.Services;

namespace UrlShortener.Api.Modules.Auth;

// Composition boundary for this module. Other modules never reference the handler or
// ApiKey entity directly — they depend only on the standard RequireAuthorization()
// extension, which works against whatever scheme is registered here.
public static class AuthModule
{
    // Self-serve key creation (POST /auth/api-keys) is unauthenticated by nature and has no
    // abuse protection (no rate limiting, no email verification) — fine for local/testing use,
    // not for a real deployment. Off unless explicitly enabled via this config key.
    public const string EnableSelfServeApiKeysConfigKey = "Auth:EnableSelfServeApiKeys";

    public static IServiceCollection AddAuthModule(this IServiceCollection services)
    {
        services.AddScoped<IApiKeyService, ApiKeyService>();

        services.AddAuthentication(ApiKeyConstants.SchemeName)
            .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyConstants.SchemeName, _ => { });

        services.AddAuthorization();

        return services;
    }

    public static IEndpointRouteBuilder MapAuthModule(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        if (configuration.GetValue(EnableSelfServeApiKeysConfigKey, defaultValue: false))
        {
            app.MapAuthEndpoints();
        }

        return app;
    }
}
