using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Auth.Dtos;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Modules.Auth.Endpoints;

// Self-serve key creation (POST /auth/api-keys) is gated behind AuthModule's
// EnableSelfServeApiKeysConfigKey flag — on in appsettings.Development.json, which is what
// LinksApiFixture boots under (WebApplicationFactory defaults to the Development environment),
// so these tests exercise the enabled path against the shared fixture. The disabled path is
// covered separately (AuthEndpointsDisabledTests) since it needs the flag forced off.
[Collection(LinksApiCollection.Name)]
public class AuthEndpointsTests(LinksApiFixture fixture)
{
    [Fact]
    public async Task PostAuthApiKeys_WithOwnerName_Returns201WithWorkingKey()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/api-keys", new CreateApiKeyRequest("Self Serve Owner", "owner@example.com"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        Assert.NotNull(body);
        Assert.Equal("Self Serve Owner", body!.OwnerName);
        Assert.False(string.IsNullOrWhiteSpace(body.ApiKey));

        using var authedClient = fixture.CreateClient();
        authedClient.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, body.ApiKey);
        var createLinkResponse = await authedClient.PostAsJsonAsync(
            "/links", new CreateLinkRequest("https://example.com/self-serve"));

        Assert.Equal(HttpStatusCode.Created, createLinkResponse.StatusCode);
    }

    [Fact]
    public async Task PostAuthApiKeys_WithoutOwnerName_Returns400()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/api-keys", new CreateApiKeyRequest(string.Empty, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
