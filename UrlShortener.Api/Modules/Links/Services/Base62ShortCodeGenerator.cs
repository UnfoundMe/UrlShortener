using System.Numerics;

namespace UrlShortener.Api.Modules.Links.Services;

// Task 2 (docs/url-shortener-tasks.md): base62-encodes a Link's identity-generated id after a
// fixed, reversible 64-bit bit-mix (XOR + rotation) so codes don't read as visibly sequential.
// Pure logic, no dependencies.
public class Base62ShortCodeGenerator : IShortCodeGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    // Fixed mix parameters: an arbitrary odd 64-bit constant (so XOR flips roughly half the
    // bits regardless of input) plus a fixed rotation amount. Both must stay constant across
    // the process lifetime/deployments since decoding depends on them.
    private const ulong MixXor = 0x9E3779B97F4A7C15UL;
    private const int MixRotate = 17;

    public string Encode(long linkId) => EncodeBase62(Mix((ulong)linkId));

    // Not part of IShortCodeGenerator: nothing resolves a link by decoding its code (that's a
    // DB lookup by ShortCode), this exists so the mix's reversibility can be verified directly.
    public long Decode(string code) => (long)Unmix(DecodeBase62(code));

    private static ulong Mix(ulong value) => BitOperations.RotateLeft(value ^ MixXor, MixRotate);

    private static ulong Unmix(ulong mixed) => BitOperations.RotateRight(mixed, MixRotate) ^ MixXor;

    private static string EncodeBase62(ulong value)
    {
        if (value == 0)
        {
            return Alphabet[0].ToString();
        }

        Span<char> buffer = stackalloc char[11]; // ceil(64 / log2(62))
        var index = buffer.Length;

        while (value > 0)
        {
            buffer[--index] = Alphabet[(int)(value % 62)];
            value /= 62;
        }

        return new string(buffer[index..]);
    }

    private static ulong DecodeBase62(string code)
    {
        ulong value = 0;
        foreach (var c in code)
        {
            var digit = Alphabet.IndexOf(c);
            if (digit < 0)
            {
                throw new ArgumentException($"'{c}' is not a valid base62 character.", nameof(code));
            }

            value = (value * 62) + (ulong)digit;
        }

        return value;
    }
}
