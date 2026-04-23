using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.E2E;

/// <summary>
/// Boots the Portal in-process with EF Core's InMemory provider. Each instance
/// gets its own database name so xUnit class fixtures don't leak state.
/// </summary>
public class PortalFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"e2e-{Guid.NewGuid():N}";

    // EF's internal service provider has to be isolated per provider, otherwise
    // both Npgsql (registered by AddMeridianInfrastructure) and InMemory (registered
    // here) end up in the same service collection and EF refuses to pick one.
    private readonly IServiceProvider _efInternalServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Connection string is required by Program.cs; the value is ignored once
        // we replace the DbContextOptions below.
        builder.UseSetting("ConnectionStrings:Meridian", "Host=test;Database=test;Username=test");
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MeridianDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<MeridianDbContext>(opts =>
                opts.UseInMemoryDatabase(_dbName)
                    .UseInternalServiceProvider(_efInternalServices));
        });

        // EnsureCreated runs after the host starts so the InMemory store has the schema
        // before the first request hits a query.
        builder.ConfigureServices(services =>
        {
            services.AddHostedService<EnsureCreatedHostedService>();
        });
    }
}

internal class EnsureCreatedHostedService : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly IServiceProvider _sp;
    public EnsureCreatedHostedService(IServiceProvider sp) => _sp = sp;
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
