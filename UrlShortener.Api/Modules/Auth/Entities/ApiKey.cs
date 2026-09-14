namespace UrlShortener.Api.Modules.Auth.Entities;

public class ApiKey
{
    public long Id { get; set; }
    public required string KeyPrefix { get; set; }
    public required string HashedKey { get; set; }
    public required string OwnerName { get; set; }
    public string? OwnerEmail { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastUsedAt { get; set; }
}
