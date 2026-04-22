using Meridian.Application.Ingestion;
using Meridian.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Worker.Jobs;

public class IngestionJob : IMeridianJob
{
    public string Name => "Ingestion";

    public async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken ct)
    {
        var logger = scopedProvider.GetRequiredService<ILogger<IngestionJob>>();
        var tenantRepo = scopedProvider.GetRequiredService<ITenantRepository>();
        var orchestrator = scopedProvider.GetRequiredService<IngestionOrchestrator>();

        var tenants = await tenantRepo.GetActiveTenantsAsync(ct);
        foreach (var tenant in tenants)
        {
            logger.LogInformation("Running ingestion for tenant {TenantName}", tenant.Name);

            var result = await orchestrator.RunForTenantAsync(tenant.Id, ct);
            if (result.IsSuccess)
            {
                var s = result.Value!;
                logger.LogInformation(
                    "Ingestion complete for {Tenant}: {Ingested} ingested, {Duplicates} duplicates, {Failed} sources failed",
                    tenant.Name, s.Ingested, s.Duplicates, s.FailedSources);
            }
            else
            {
                logger.LogError("Ingestion failed for {Tenant}: {Error}", tenant.Name, result.Error);
            }
        }
    }
}
