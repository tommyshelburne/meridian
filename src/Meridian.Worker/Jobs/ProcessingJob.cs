using Meridian.Application.Pipeline;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Worker.Jobs;

// Runs the post-ingest pipeline: pulls Status=New opportunities for each
// active tenant and walks them through scoring → enrichment → CRM → enrollment.
// Scheduled between IngestionJob (06:00 UTC) and the 2-hour SequenceJob loop
// so freshly-ingested opps are ready to send within the same business day.
public class ProcessingJob : IMeridianJob
{
    public string Name => "Processing";

    public async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken ct)
    {
        var logger = scopedProvider.GetRequiredService<ILogger<ProcessingJob>>();
        var tenantRepo = scopedProvider.GetRequiredService<ITenantRepository>();
        var tenantContext = scopedProvider.GetRequiredService<ITenantContext>();
        var pipeline = scopedProvider.GetRequiredService<MeridianPipelineService>();

        var tenants = await tenantRepo.GetActiveTenantsAsync(ct);
        foreach (var tenant in tenants)
        {
            tenantContext.SetTenant(tenant.Id);
            var result = await pipeline.ProcessNewOpportunitiesAsync(tenant.Id, ct);
            if (result.IsSuccess)
            {
                var s = result.Value!;
                if (s.Processed > 0)
                    logger.LogInformation(
                        "Processing complete for {Tenant}: {Processed} opps, " +
                        "{Pursue} pursue, {Partner} partner, {NoBid} no-bid, " +
                        "{Contacts} contacts, {Deals} deals, {Enrollments} enrollments",
                        tenant.Name, s.Processed, s.Pursue, s.Partner, s.NoBid,
                        s.ContactsEnriched, s.DealsCreated, s.Enrollments);
            }
            else
            {
                logger.LogError("Processing failed for {Tenant}: {Error}", tenant.Name, result.Error);
            }
        }
    }
}
