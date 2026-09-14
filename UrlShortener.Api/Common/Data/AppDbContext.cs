using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Modules.Analytics.Entities;
using UrlShortener.Api.Modules.Auth.Entities;
using UrlShortener.Api.Modules.Links.Entities;

namespace UrlShortener.Api.Common.Data;

// Shared across modules: one database, one transaction boundary. Each module owns its own
// entity + IEntityTypeConfiguration<T>; this context only aggregates DbSets and applies them.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Link> Links => Set<Link>();
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
