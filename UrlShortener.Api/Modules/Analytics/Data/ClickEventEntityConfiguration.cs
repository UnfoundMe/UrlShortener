using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrlShortener.Api.Modules.Analytics.Entities;

namespace UrlShortener.Api.Modules.Analytics.Data;

public class ClickEventEntityConfiguration : IEntityTypeConfiguration<ClickEvent>
{
    public void Configure(EntityTypeBuilder<ClickEvent> builder)
    {
        builder.HasIndex(c => c.LinkId);
    }
}
