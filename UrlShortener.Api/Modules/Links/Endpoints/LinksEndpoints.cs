using System.Globalization;
using System.Security.Claims;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Modules.Links.Services;

namespace UrlShortener.Api.Modules.Links.Endpoints;

public static class LinksEndpoints
{
    public static IEndpointRouteBuilder MapLinksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/links", async (
                CreateLinkRequest request,
                ILinkService linkService,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var apiKeyId = long.Parse(
                    user.FindFirstValue(ClaimTypes.NameIdentifier)!,
                    CultureInfo.InvariantCulture);

                try
                {
                    var link = await linkService.CreateAsync(request.OriginalUrl, apiKeyId, cancellationToken);
                    var response = new CreateLinkResponse(link.ShortCode, link.OriginalUrl, link.CreatedAt);

                    return Results.Created($"/{link.ShortCode}", response);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireAuthorization()
            .AddEndpointFilter<IpRateLimitFilter>();

        return app;
    }
}
