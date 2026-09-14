using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Auth.Entities;

namespace UrlShortener.Api.Modules.Auth.Services;

// Task 3 (docs/url-shortener-tasks.md): generate a random secret, store its SHA-256 hash plus a
// plaintext KeyPrefix for fast candidate lookup, verify by prefix-then-hash on validate.
public class ApiKeyService(AppDbContext dbContext) : IApiKeyService
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    // KeyPrefix column is HasMaxLength(12) — keep comfortably under that.
    private const int PrefixLength = 10;
    private const int SecretLength = 32;

    private readonly AppDbContext _dbContext = dbContext;

    public async Task<(string RawKey, ApiKey Entity)> GenerateAsync(string ownerName, string? ownerEmail, CancellationToken cancellationToken = default)
    {
        var prefix = RandomNumberGenerator.GetString(Alphabet, PrefixLength);
        var secret = RandomNumberGenerator.GetString(Alphabet, SecretLength);
        var rawKey = prefix + secret;

        var entity = new ApiKey
        {
            KeyPrefix = prefix,
            HashedKey = Hash(rawKey),
            OwnerName = ownerName,
            OwnerEmail = ownerEmail,
        };

        _dbContext.ApiKeys.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (rawKey, entity);
    }

    public async Task<ApiKey?> ValidateAsync(string rawKey, CancellationToken cancellationToken = default)
    {
        if (rawKey.Length <= PrefixLength)
        {
            return null;
        }

        var prefix = rawKey[..PrefixLength];

        var candidate = await _dbContext.ApiKeys
            .Where(k => k.KeyPrefix == prefix && k.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null || !FixedTimeHashEquals(rawKey, candidate.HashedKey))
        {
            return null;
        }

        candidate.LastUsedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return candidate;
    }

    private static bool FixedTimeHashEquals(string rawKey, string storedHashedKey)
    {
        var computedHash = Convert.FromHexString(Hash(rawKey));
        var storedHash = Convert.FromHexString(storedHashedKey);

        return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
    }

    private static string Hash(string rawKey) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
}
