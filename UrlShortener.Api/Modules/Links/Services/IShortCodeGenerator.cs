namespace UrlShortener.Api.Modules.Links.Services;

public interface IShortCodeGenerator
{
    // Encodes a Link's identity-generated id into a short, non-sequential-looking code.
    string Encode(long linkId);
}
