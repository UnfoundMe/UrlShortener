using UrlShortener.Api.Modules.Links.Abstractions;
using UrlShortener.Api.Modules.Links.Endpoints;
using UrlShortener.Api.Modules.Links.Services;

namespace UrlShortener.Api.Modules.Links;

// Composition boundary for this module: everything the rest of the app needs to know about
// the Links module goes through these two extension methods, called once from Program.cs.
public static class LinksModule
{
    public static IServiceCollection AddLinksModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IShortCodeGenerator, Base62ShortCodeGenerator>();
        services.AddScoped<ILinkCacheService, RedisLinkCacheService>();
        services.AddScoped<ILinkService, LinkService>();
        services.AddScoped<ILinkClickCounterUpdater, LinkClickCounterUpdater>();
        services.AddScoped<ILinkLookup, LinkLookup>();

        services.Configure<RateLimitOptions>(configuration.GetSection("RateLimiting"));
        // Singleton: wraps the already-singleton IConnectionMultiplexer and holds no per-request
        // state, and AddEndpointFilter<IpRateLimitFilter>() builds its filter from the root
        // service provider at endpoint-construction time, so a scoped dependency here would fail.
        services.AddSingleton<IIpRateLimiter, RedisIpRateLimiter>();

        return services;
    }

    public static IEndpointRouteBuilder MapLinksModule(this IEndpointRouteBuilder app)
    {
        app.MapLinksEndpoints();
        app.MapRedirectEndpoints();

        return app;
    }
}
