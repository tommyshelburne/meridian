using Meridian.Application.Opportunities;
using Meridian.Application.Ports;
using Microsoft.AspNetCore.DataProtection;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Health;
using Meridian.Infrastructure.Ingestion;
using Meridian.Infrastructure.Persistence;
using Meridian.Worker;
using Meridian.Worker.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// /health is the only HTTP surface the Worker exposes. Background jobs do
// the real work. Default to a non-conflict port so this co-locates cleanly
// with the Portal in dev/prod. Operator overrides via ASPNETCORE_URLS or
// the Urls config key as usual.
if (string.IsNullOrEmpty(builder.Configuration["Urls"])
    && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:9090");
}

// Infrastructure: DB, repositories, scoring
var connectionString = builder.Configuration.GetConnectionString("Meridian")
    ?? throw new InvalidOperationException("ConnectionStrings:Meridian is required");
builder.Services.AddMeridianInfrastructure(connectionString, builder.Configuration);

// Data Protection — required by ISecretProtector to decrypt per-tenant
// outbound API keys, CRM tokens, and OIDC client secrets. The Worker and
// Portal MUST share the same keyring in production (PersistKeysToFileSystem
// pointed at a shared volume); otherwise the Portal-encrypted secrets are
// unreadable here. Local dev uses default ephemeral storage which is fine
// because secrets aren't persisted across restarts in dev anyway.
var dpBuilder = builder.Services.AddDataProtection().SetApplicationName("Meridian");
var dpKeyDir = builder.Configuration["DataProtection:KeyDirectory"];
if (!string.IsNullOrWhiteSpace(dpKeyDir))
    dpBuilder.PersistKeysToFileSystem(new DirectoryInfo(dpKeyDir));

// Dev-only enricher swap so the soft-launch dry-run produces a contact
// without needing real SAM.gov / USASpending API keys. The real enrichers
// hang on outbound HTTP calls when their keys are missing (~100s timeouts
// each), so we remove them entirely in dev rather than ordering around them.
if (builder.Environment.IsDevelopment())
{
    builder.Services.RemoveAll<IPocEnricher>();
    builder.Services.AddTransient<IPocEnricher, DevSyntheticPocEnricher>();
}

// Health endpoint provider — captures version + build date at startup,
// returns current timestamp on each request.
builder.Services.AddSingleton<HealthInfo>();

// Worker jobs
builder.Services.AddSingleton<IMeridianJob, IngestionJob>();
builder.Services.AddSingleton<IMeridianJob, ProcessingJob>();
builder.Services.AddSingleton<IMeridianJob, SequenceJob>();
builder.Services.AddSingleton<IMeridianJob, BidMonitorJob>();
builder.Services.AddSingleton<IMeridianJob, CrmTokenRefreshJob>();

// `--smoke <tenant-slug>` mode: bootstrap DI, seed the soft-launch scaffold +
// sample opportunities for the named tenant, run ProcessingJob then SequenceJob
// once, and exit. Used to verify the post-ingest pipeline end-to-end without
// waiting for the scheduled cadence.
if (args.Length >= 2 && args[0] == "--smoke")
{
    var smokeHost = builder.Build();
    await RunSmokeAsync(smokeHost.Services, args[1]);
    return;
}

// `--hash-password <pwd>`: print a BCrypt hash compatible with AuthService.
// Useful for direct SQL password resets when the email-based reset path is
// unavailable (e.g. tenant has no OutboundConfiguration so reset emails are
// dropped). Dev-only — production should use the email-based flow.
if (args.Length >= 2 && args[0] == "--hash-password")
{
    var hashHost = builder.Build();
    var hasher = hashHost.Services.GetRequiredService<IPasswordHasher>();
    Console.WriteLine(hasher.Hash(args[1]));
    return;
}

// Hosted service
builder.Services.AddHostedService<MeridianWorker>();

var app = builder.Build();

app.MapGet("/health", (HealthInfo info) => Results.Json(info.Build()));

app.Run();
return;

static async Task RunSmokeAsync(IServiceProvider services, string tenantSlug)
{
    using var scope = services.CreateScope();
    var sp = scope.ServiceProvider;
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var db = sp.GetRequiredService<MeridianDbContext>();

    var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == tenantSlug);
    if (tenant is null)
    {
        logger.LogError("Tenant with slug '{Slug}' not found", tenantSlug);
        return;
    }
    tenantContext.SetTenant(tenant.Id);
    logger.LogInformation("Smoke run for tenant {Tenant} ({Id})", tenant.Name, tenant.Id);

    var seed = sp.GetRequiredService<DevSeedService>();
    var scaffold = await seed.SeedOutreachScaffoldAsync(tenant.Id, CancellationToken.None);
    logger.LogInformation("Outreach scaffold: {Result}", scaffold.IsSuccess ? "ok" : scaffold.Error);
    var samples = await seed.SeedSampleOpportunitiesAsync(tenant.Id, CancellationToken.None);
    logger.LogInformation("Sample opportunities: {Added} added", samples.Value);

    var processing = new ProcessingJob();
    await processing.ExecuteAsync(sp, CancellationToken.None);

    var sequence = new SequenceJob();
    await sequence.ExecuteAsync(sp, CancellationToken.None);

    logger.LogInformation("Smoke run complete for {Tenant}", tenant.Name);
}
