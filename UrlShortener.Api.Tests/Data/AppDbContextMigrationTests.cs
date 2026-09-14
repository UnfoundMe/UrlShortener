using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Links.Entities;
using UrlShortener.Api.Tests.Fixtures;

namespace UrlShortener.Api.Tests.Data;

[Collection(PostgresCollection.Name)]
public class AppDbContextMigrationTests(PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task Migrate_CreatesExpectedTablesAndIndexes()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var tableNames = await QueryStringColumnAsync(
            context, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'");

        Assert.Contains("Links", tableNames);
        Assert.Contains("ClickEvents", tableNames);
        Assert.Contains("ApiKeys", tableNames);

        var indexNames = await QueryStringColumnAsync(
            context, "SELECT indexname FROM pg_indexes WHERE schemaname = 'public'");

        Assert.Contains("IX_Links_ShortCode", indexNames);
        Assert.Contains("IX_ClickEvents_LinkId", indexNames);
        Assert.Contains("IX_ApiKeys_KeyPrefix", indexNames);
    }

    [Fact]
    public async Task Migrate_AppliedTwice_IsIdempotent()
    {
        await using var first = CreateContext();
        await first.Database.MigrateAsync();

        await using var second = CreateContext();
        // Should not throw even though the schema already exists — EF tracks applied
        // migrations in __EFMigrationsHistory and this is a no-op on the second call.
        await second.Database.MigrateAsync();
    }

    [Fact]
    public async Task DuplicateShortCode_ThrowsUniqueConstraintViolation()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        context.Links.Add(new Link
        {
            ShortCode = "dup-test-code",
            OriginalUrl = "https://example.com/1",
            CreatedByApiKeyId = 1,
        });
        await context.SaveChangesAsync();

        context.Links.Add(new Link
        {
            ShortCode = "dup-test-code",
            OriginalUrl = "https://example.com/2",
            CreatedByApiKeyId = 1,
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
    }

    private static async Task<List<string>> QueryStringColumnAsync(AppDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
