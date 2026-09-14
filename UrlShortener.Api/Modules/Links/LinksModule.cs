using UrlShortener.Api.Modules.Links.Abstractions;
using UrlShortener.Api.Modules.Links.Endpoints;
using UrlShortener.Api.Modules.Links.Services;

namespace UrlShortener.Api.Modules.Links;

// Composition boundary for this module: everything the rest of the app needs to know about
// the Links module goes through these two extension methods, called once from Program.cs.
public static class LinksModule
{
    public static IServiceCollection AddLinksModule(this IServiceCollection services)
    {
        services.AddScoped<IShortCodeGenerator, Base62ShortCodeGenerator>();
        services.AddScoped<ILinkCacheService, RedisLinkCacheService>();
        services.AddScoped<ILinkService, LinkService>();
        services.AddScoped<ILinkClickCounterUpdater, LinkClickCounterUpdater>();
        services.AddScoped<ILinkLookup, LinkLookup>();

        return services;
    }

    public static IEndpointRouteBuilder MapLinksModule(this IEndpointRouteBuilder app)
    {
        app.MapLinksEndpoints();
        app.MapRedirectEndpoints();

        return app;
    }
}
