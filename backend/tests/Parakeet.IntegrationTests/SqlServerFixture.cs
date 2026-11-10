using Microsoft.EntityFrameworkCore;
using Parakeet.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Parakeet.IntegrationTests;

/// <summary>
/// Starts one SQL Server container for the whole test run. Each test asks for its own
/// freshly migrated database on it, so tests can't affect each other.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string NewDatabaseConnectionString()
        => _container.GetConnectionString().Replace("Database=master", $"Database=test_{Guid.NewGuid():N}");

    public static AppDbContext Context(string connectionString)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);

    public async Task<string> MigratedDatabaseAsync()
    {
        var connectionString = NewDatabaseConnectionString();
        await using var db = Context(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }
}

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}
