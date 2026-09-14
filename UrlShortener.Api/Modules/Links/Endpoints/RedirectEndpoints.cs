using UrlShortener.Api.Modules.Analytics.Abstractions;
using UrlShortener.Api.Modules.Links.Services;

namespace UrlShortener.Api.Modules.Links.Endpoints;

public static class RedirectEndpoints
{
    public static IEndpointRouteBuilder MapRedirectEndpoints(this IEndpointRouteBuilder app)
    {
        // Intentionally unauthenticated — it's a public redirect.
        app.MapGet("/{code}", async (
            string code,
            ILinkService linkService,
            IClickEventRecorder clickEventRecorder,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var resolution = await linkService.ResolveAsync(code, cancellationToken);

            if (resolution is null)
            {
                return Results.NotFound();
            }

            // A bounded channel write, not awaited beyond the synchronous TryWrite inside
            // Record — the redirect response never waits on analytics.
            clickEventRecorder.Record(
                resolution.LinkId,
                HeaderOrNull(httpContext.Request.Headers, "Referer"),
                HeaderOrNull(httpContext.Request.Headers, "User-Agent"),
                httpContext.Connection.RemoteIpAddress?.ToString());

            return Results.Redirect(resolution.OriginalUrl, permanent: false);
        });

        return app;
    }

    private static string? HeaderOrNull(IHeaderDictionary headers, string name) =>
        headers.TryGetValue(name, out var values) && values.Count > 0 ? values.ToString() : null;
}
