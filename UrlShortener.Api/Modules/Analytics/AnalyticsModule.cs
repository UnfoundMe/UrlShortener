using UrlShortener.Api.Modules.Analytics.Abstractions;
using UrlShortener.Api.Modules.Analytics.BackgroundServices;
using UrlShortener.Api.Modules.Analytics.Channels;
using UrlShortener.Api.Modules.Analytics.Endpoints;
using UrlShortener.Api.Modules.Analytics.Services;

namespace UrlShortener.Api.Modules.Analytics;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        services.AddSingleton<ClickEventChannel>();
        services.AddSingleton<IClickEventRecorder, ClickEventRecorder>();
        services.AddHostedService<ClickEventWriter>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();

        return services;
    }

    public static IEndpointRouteBuilder MapAnalyticsModule(this IEndpointRouteBuilder app)
    {
        app.MapAnalyticsEndpoints();

        return app;
    }
}
