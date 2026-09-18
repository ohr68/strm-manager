using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

        // Pooling=False is what makes each factory's database privately owned. With pooling
        // on, a closed connection is parked in a process-wide pool and only
        // SqliteConnection.ClearAllPools() can release it before the file is deleted - but
        // that clears EVERY factory's pool, and test classes run in parallel, so one class
        // finishing could dispose a pooled sqlite3 handle another class had just rented
        // (ObjectDisposedException 'SQLitePCL.sqlite3', surfacing as random 500s). Without a
        // pool there is nothing shared: a connection closes with the DbContext that opened
        // it, and Dispose below only ever touches this factory's own files. Production keeps
        // pooling (CatalogModule is unchanged); the test SQLite file, WAL mode, foreign keys
        // and busy timeout all behave the same, connections are just not reused.
        builder.UseSetting("ConnectionStrings:Database", $"Data Source={_databasePath};Pooling=False");

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

        // No pool to clear (see ConfigureWebHost): the host and every derived factory were
        // disposed above, which closed every connection this factory ever opened, so the
        // file is free to delete. If a test ever leaks a DbContext/connection this fails
        // loudly here instead of being papered over by a process-global pool clear.
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
