using System.Net.Http.Json;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Links.Dtos;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Observability;

// Task 9 (docs/url-shortener-tasks.md): asserts the observability log events actually fire, via
// CapturingLoggerProvider (see LinksApiFixture) rather than manual `docker compose logs`
// verification — chosen because the app already boots through a real WebApplicationFactory in
// this collection, so wiring in a capturing ILoggerProvider was cheap and gives a repeatable,
// automated check instead of a one-time manual one.
[Collection(LinksApiCollection.Name)]
public class ObservabilityTests(LinksApiFixture fixture)
{
    [Fact]
    public async Task InvalidApiKey_LogsFailureReasonWithoutTheRawKey()
    {
        using var client = fixture.CreateClient();
        var rawKey = $"unique-invalid-key-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, rawKey);

        await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com"));

        var entry = await fixture.WaitForLogAsync(e =>
            e.Category.Contains("ApiKeyAuthenticationHandler") &&
            e.Message.Contains("invalid or inactive key"));

        Assert.DoesNotContain(rawKey, entry.Message);
        Assert.DoesNotContain(fixture.LogEntries.Entries, e => e.Message.Contains(rawKey));
    }

    [Fact]
    public async Task Redirect_LogsCacheMissThenCacheHit()
    {
        using var client = fixture.CreateClient(allowAutoRedirect: false);
        var apiKey = await fixture.CreateApiKeyAsync();
        client.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        var createResponse = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/observability"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var shortCode = created!.ShortCode;

        var first = await client.GetAsync($"/{shortCode}");
        Assert.Equal(System.Net.HttpStatusCode.Found, first.StatusCode);
        // Serilog's writeToProviders bridge (Program.cs) re-renders string template args with
        // quotes when forwarding to other ILoggerProviders, so match loosely rather than on an
        // exact quoted/unquoted substring.
        await fixture.WaitForLogAsync(e => e.Message.Contains("Cache miss") && e.Message.Contains(shortCode));

        var second = await client.GetAsync($"/{shortCode}");
        Assert.Equal(System.Net.HttpStatusCode.Found, second.StatusCode);
        await fixture.WaitForLogAsync(e => e.Message.Contains("Cache hit") && e.Message.Contains(shortCode));
    }

    [Fact]
    public async Task RedirectClick_LogsFlushedBatchSizeAndDuration()
    {
        using var client = fixture.CreateClient(allowAutoRedirect: false);
        var apiKey = await fixture.CreateApiKeyAsync();
        client.DefaultRequestHeaders.Add(ApiKeyConstants.HeaderName, apiKey);

        var createResponse = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/observability-flush"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();

        await client.GetAsync($"/{created!.ShortCode}");

        var entry = await fixture.WaitForLogAsync(
            e => e.Category.Contains("ClickEventWriter") && e.Message.Contains("Flushed a batch of"),
            timeout: TimeSpan.FromSeconds(10));

        Assert.Contains("click events in", entry.Message);
        Assert.Contains("ms", entry.Message);
    }
}
