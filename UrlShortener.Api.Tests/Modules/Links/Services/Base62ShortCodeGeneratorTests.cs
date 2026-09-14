using System.Text.RegularExpressions;
using UrlShortener.Api.Modules.Links.Services;

namespace UrlShortener.Api.Tests.Modules.Links.Services;

public partial class Base62ShortCodeGeneratorTests
{
    private readonly Base62ShortCodeGenerator _generator = new();

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(41L)]
    [InlineData(123456789L)]
    [InlineData(long.MaxValue)]
    public void Encode_ThenDecode_RoundTrips(long linkId)
    {
        var code = _generator.Encode(linkId);

        Assert.Equal(linkId, _generator.Decode(code));
    }

    [Fact]
    public void Encode_First10000SequentialIds_ProduceNoCollisions()
    {
        var codes = new HashSet<string>();

        for (long id = 1; id <= 10_000; id++)
        {
            Assert.True(codes.Add(_generator.Encode(id)), $"Collision detected for id {id}.");
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(41L)]
    [InlineData(123456789L)]
    [InlineData(long.MaxValue)]
    public void Encode_OutputContainsOnlyAlphanumericCharacters(long linkId)
    {
        var code = _generator.Encode(linkId);

        Assert.Matches(AlphanumericRegex(), code);
    }

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex AlphanumericRegex();
}
