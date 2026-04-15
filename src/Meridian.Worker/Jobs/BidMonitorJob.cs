using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Worker.Jobs;

public class BidMonitorJob : IMeridianJob
{
    public string Name => "BidMonitor";

    public async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken ct)
    {
        var logger = scopedProvider.GetRequiredService<ILogger<BidMonitorJob>>();
        var tenantRepo = scopedProvider.GetRequiredService<ITenantRepository>();
        var tenantContext = scopedProvider.GetRequiredService<ITenantContext>();
        var bidMonitor = scopedProvider.GetRequiredService<IBidMonitor>();
        var opportunityRepo = scopedProvider.GetRequiredService<IOpportunityRepository>();
        var auditLog = scopedProvider.GetRequiredService<IAuditLog>();

        var tenants = await tenantRepo.GetActiveTenantsAsync(ct);
        foreach (var tenant in tenants)
        {
            tenantContext.SetTenant(tenant.Id);
            var watched = await opportunityRepo.GetWatchedAsync(tenant.Id, ct);
            if (watched.Count == 0) continue;

            var result = await bidMonitor.CheckForUpdatesAsync(watched, ct);
            if (!result.IsSuccess)
            {
                logger.LogError("Bid monitor failed for {Tenant}: {Error}", tenant.Name, result.Error);
                continue;
            }

            foreach (var update in result.Value!)
            {
                var opp = await opportunityRepo.GetByExternalIdAsync(tenant.Id, update.ExternalId, ct);
                if (opp is null) continue;

                opp.RecordAmendment(update.AmendedAt);

                await auditLog.AppendAsync(Domain.Audit.AuditEvent.Record(
                    tenant.Id, "Opportunity", opp.Id, "AmendmentDetected", "system",
                    System.Text.Json.JsonSerializer.Serialize(new { update.ExternalId, update.AmendedAt, update.NewDeadline })), ct);

                logger.LogInformation("Amendment detected for {Title}", opp.Title);
            }

            await opportunityRepo.SaveChangesAsync(ct);
        }
    }
}
