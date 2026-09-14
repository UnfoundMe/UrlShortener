namespace UrlShortener.Api.Tests.Fixtures;

[CollectionDefinition(Name)]
public class RateLimitedLinksApiCollection : ICollectionFixture<RateLimitedLinksApiFixture>
{
    public const string Name = "RateLimitedLinksApi";
}
