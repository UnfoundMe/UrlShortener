using UrlShortener.Api.Modules.Analytics.Services;

namespace UrlShortener.Api.Modules.Analytics.Endpoints;

public static class AnalyticsEndpoints
{
    private const int DefaultRecentClicksLimit = 20;
    private const int MaxRecentClicksLimit = 100;

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/links/{code}/analytics", async (
                string code,
                int? limit,
                IAnalyticsService analyticsService,
                CancellationToken cancellationToken) =>
            {
                var recentClicksLimit = Math.Clamp(limit ?? DefaultRecentClicksLimit, 1, MaxRecentClicksLimit);
                var analytics = await analyticsService.GetAnalyticsAsync(code, recentClicksLimit, cancellationToken);

                return analytics is null ? Results.NotFound() : Results.Ok(analytics);
            })
            .RequireAuthorization();

        return app;
    }
}
