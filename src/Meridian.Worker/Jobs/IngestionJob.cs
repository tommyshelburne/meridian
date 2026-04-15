using Meridian.Application.Pipeline;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
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
        var tenantContext = scopedProvider.GetRequiredService<ITenantContext>();
        var pipeline = scopedProvider.GetRequiredService<MeridianPipelineService>();

        var tenants = await tenantRepo.GetActiveTenantsAsync(ct);
        foreach (var tenant in tenants)
        {
            tenantContext.SetTenant(tenant.Id);
            logger.LogInformation("Running ingestion for tenant {TenantName}", tenant.Name);

            var result = await pipeline.ExecuteAsync(tenant.Id, ct);
            if (result.IsSuccess)
            {
                var s = result.Value!;
                logger.LogInformation(
                    "Ingestion complete for {Tenant}: {Ingested} ingested, {Pursue} pursue, {Partner} partner, {NoBid} no-bid, {Contacts} contacts enriched, {Deals} deals created",
                    tenant.Name, s.Ingested, s.Pursue, s.Partner, s.NoBid, s.ContactsEnriched, s.DealsCreated);
            }
            else
            {
                logger.LogError("Ingestion failed for {Tenant}: {Error}", tenant.Name, result.Error);
            }
        }
    }
}
