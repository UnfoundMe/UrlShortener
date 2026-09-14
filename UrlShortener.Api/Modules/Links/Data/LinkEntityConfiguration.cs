using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrlShortener.Api.Modules.Links.Entities;

namespace UrlShortener.Api.Modules.Links.Data;

public class LinkEntityConfiguration : IEntityTypeConfiguration<Link>
{
    public void Configure(EntityTypeBuilder<Link> builder)
    {
        builder.Property(l => l.ShortCode).HasMaxLength(16).IsRequired();
        builder.HasIndex(l => l.ShortCode).IsUnique();

        builder.Property(l => l.OriginalUrl).IsRequired();
    }
}
