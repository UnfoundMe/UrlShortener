using System.Globalization;
using UrlShortener.Api.Modules.Links.Services;

namespace UrlShortener.Api.Modules.Links.Endpoints;

// Attached only to POST /links (see LinksEndpoints) via AddEndpointFilter — the redirect
// endpoint never goes through this filter.
public class IpRateLimitFilter(IIpRateLimiter rateLimiter) : IEndpointFilter
{
    private readonly IIpRateLimiter _rateLimiter = rateLimiter;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var clientKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var decision = await _rateLimiter.CheckAsync(clientKey, httpContext.RequestAborted);
        if (!decision.IsAllowed)
        {
            httpContext.Response.Headers["Retry-After"] = decision.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return Results.Json(
                new { error = "Rate limit exceeded. Try again later." },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        return await next(context);
    }
}
