using Npgsql;

namespace UrlShortener.Api.Tests.Fixtures;

[Collection(PostgresCollection.Name)]
public class PostgresFixtureTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Container_IsReachable()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(1, result);
    }
}
