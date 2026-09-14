using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrlShortener.Api.Modules.Auth.Entities;

namespace UrlShortener.Api.Modules.Auth.Data;

public class ApiKeyEntityConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.Property(k => k.KeyPrefix).HasMaxLength(12).IsRequired();
        builder.HasIndex(k => k.KeyPrefix);

        builder.Property(k => k.HashedKey).HasMaxLength(64).IsRequired();
        builder.Property(k => k.OwnerName).IsRequired();
    }
}
