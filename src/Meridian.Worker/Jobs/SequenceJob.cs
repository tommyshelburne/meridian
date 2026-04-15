using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Worker.Jobs;

public class SequenceJob : IMeridianJob
{
    public string Name => "Sequence";

    public async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken ct)
    {
        var logger = scopedProvider.GetRequiredService<ILogger<SequenceJob>>();
        var tenantRepo = scopedProvider.GetRequiredService<ITenantRepository>();
        var tenantContext = scopedProvider.GetRequiredService<ITenantContext>();
        var sequenceEngine = scopedProvider.GetRequiredService<ISequenceEngine>();

        var tenants = await tenantRepo.GetActiveTenantsAsync(ct);
        foreach (var tenant in tenants)
        {
            tenantContext.SetTenant(tenant.Id);
            var result = await sequenceEngine.ProcessDueEnrollmentsAsync(tenant.Id, ct);
            if (result.IsSuccess)
                logger.LogInformation("Sequence job sent {Count} emails for {Tenant}", result.Value, tenant.Name);
            else
                logger.LogError("Sequence job failed for {Tenant}: {Error}", tenant.Name, result.Error);
        }
    }
}
