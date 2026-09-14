namespace UrlShortener.Api.Modules.Links.Services;

public class RateLimitOptions
{
    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;
}
