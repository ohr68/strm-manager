using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"strm-manager-tests-{Guid.NewGuid():N}.db");
    private readonly string _strmRootPath = Path.Combine(Path.GetTempPath(), $"strm-manager-tests-strm-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Database", $"Data Source={_databasePath}");

        // Never write real .strm output into the repo's data/stream directory - every
        // factory instance gets its own throwaway temp root, cleaned up on Dispose.
        builder.UseSetting("Strm:RootPath", _strmRootPath);

        // Scheduling's BackgroundServices must not run against a test database - most
        // tests act as their own "caller" against a fully synchronous, deterministic
        // pipeline, and a live worker racing to claim/process the same episodes would
        // make tests flaky (see ADR-012/section 52). Dedicated Scheduling tests enable
        // it explicitly via factory.WithWebHostBuilder(...).
        builder.UseSetting("Scheduling:Enabled", "false");

        // Never hit live Cinemeta/FrostStream or spawn a real ffprobe process from the
        // test suite - each defaults to a safe/deterministic no-op (see the individual
        // fakes). Tests that need specific behavior use factory.WithWebHostBuilder(...)
        // to register their own isolated fake instead of mutating this shared one.
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IMetadataProvider, FakeMetadataProvider>();
            services.AddSingleton<IStreamProvider, FakeStreamProvider>();
            services.AddSingleton<IMediaValidator, FakeMediaValidator>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        if (Directory.Exists(_strmRootPath))
        {
            Directory.Delete(_strmRootPath, recursive: true);
        }
    }
}
