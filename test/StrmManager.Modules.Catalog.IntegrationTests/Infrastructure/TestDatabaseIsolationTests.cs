using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Infrastructure.Database;

namespace StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

/// <summary>
/// Guards the test-database ownership model ApiWebApplicationFactory relies on: every
/// factory owns its own SQLite file and must not use the process-wide connection pool.
/// If pooling were silently switched back on, ApiWebApplicationFactory would have nothing
/// releasing its file handles (ClearAllPools() was removed because it disposes OTHER test
/// classes' pooled connections mid-use) and the intermittent
/// ObjectDisposedException 'SQLitePCL.sqlite3' failures would return, one random test at a
/// time - so assert it directly against the connection string the host really resolved.
/// </summary>
public class TestDatabaseIsolationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private SqliteConnectionStringBuilder ResolvedConnectionString()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        return new SqliteConnectionStringBuilder(context.Database.GetConnectionString());
    }

    [Fact]
    public void TestHost_UsesAnUnpooledConnectionToItsOwnDatabaseFile()
    {
        SqliteConnectionStringBuilder connectionString = ResolvedConnectionString();

        Assert.False(connectionString.Pooling);
        Assert.Contains("strm-manager-tests-", connectionString.DataSource, StringComparison.Ordinal);
        Assert.EndsWith(".db", connectionString.DataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TestHost_StillGetsTheProductionSqliteOptions()
    {
        // Pooling=False is appended to the test's connection string; CatalogModule must
        // still layer its own foreign-key enforcement and busy timeout on top of it.
        SqliteConnectionStringBuilder connectionString = ResolvedConnectionString();

        Assert.True(connectionString.ForeignKeys);
        Assert.Equal(30, connectionString.DefaultTimeout);
    }

    [Fact]
    public void TwoFactories_NeverShareADatabaseFile()
    {
        using var otherFactory = new ApiWebApplicationFactory();
        using IServiceScope otherScope = otherFactory.Services.CreateScope();
        string otherPath = new SqliteConnectionStringBuilder(
            otherScope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.GetConnectionString()).DataSource;

        Assert.NotEqual(ResolvedConnectionString().DataSource, otherPath);
    }
}
