using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Auth.Services;

namespace UrlShortener.Api.Modules.Auth.Handlers;

// Task 3 (docs/url-shortener-tasks.md): reads the X-Api-Key header, validates it via
// IApiKeyService, and builds a ClaimsPrincipal on success. Never throws — auth failures map to
// AuthenticateResult.NoResult()/Fail() so ASP.NET Core turns them into a 401, not a 500.
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyService apiKeyService)
    : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>(options, logger, encoder)
{
    private readonly IApiKeyService _apiKeyService = apiKeyService;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyConstants.HeaderName, out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var rawKey = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = await _apiKeyService.ValidateAsync(rawKey, Context.RequestAborted);
        if (apiKey is null)
        {
            // Task 9 (docs/url-shortener-tasks.md): log the failure reason for audit/alerting,
            // never the raw key itself.
            Logger.LogWarning("API key authentication failed: {Reason}.", "invalid or inactive key");
            return AuthenticateResult.Fail("Invalid or inactive API key.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, apiKey.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, apiKey.OwnerName),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
