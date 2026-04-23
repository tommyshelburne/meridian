using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Worker.Jobs;

public class ReplyMonitorJob : IMeridianJob
{
    public string Name => "ReplyMonitor";

    public async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken ct)
    {
        var logger = scopedProvider.GetRequiredService<ILogger<ReplyMonitorJob>>();
        var tenantRepo = scopedProvider.GetRequiredService<ITenantRepository>();
        var tenantContext = scopedProvider.GetRequiredService<ITenantContext>();
        var inboxMonitor = scopedProvider.GetRequiredService<IInboxMonitor>();
        var processor = scopedProvider.GetRequiredService<ReplyProcessor>();

        var tenants = await tenantRepo.GetActiveTenantsAsync(ct);
        foreach (var tenant in tenants)
        {
            tenantContext.SetTenant(tenant.Id);
            var since = DateTimeOffset.UtcNow.AddHours(-4);

            var fetchResult = await inboxMonitor.CheckForRepliesAsync(since, ct);
            if (!fetchResult.IsSuccess)
            {
                logger.LogError("Reply monitor fetch failed for {Tenant}: {Error}", tenant.Name, fetchResult.Error);
                continue;
            }

            var processResult = await processor.ProcessAsync(tenant.Id, fetchResult.Value!, ct);
            if (!processResult.IsSuccess)
            {
                logger.LogError("Reply processor failed for {Tenant}: {Error}", tenant.Name, processResult.Error);
                continue;
            }

            var summary = processResult.Value!;
            logger.LogInformation(
                "Reply monitor for {Tenant}: matched-by-message-id={MessageIdMatches}, matched-by-subject={SubjectMatches}, unmatched={Unmatched}",
                tenant.Name, summary.MatchedByMessageId, summary.MatchedBySubject, summary.Unmatched);
        }
    }
}
