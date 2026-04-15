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
        var outreachRepo = scopedProvider.GetRequiredService<IOutreachRepository>();
        var auditLog = scopedProvider.GetRequiredService<IAuditLog>();

        var tenants = await tenantRepo.GetActiveTenantsAsync(ct);
        foreach (var tenant in tenants)
        {
            tenantContext.SetTenant(tenant.Id);
            var since = DateTimeOffset.UtcNow.AddHours(-4);
            var result = await inboxMonitor.CheckForRepliesAsync(since, ct);
            if (!result.IsSuccess)
            {
                logger.LogError("Reply monitor failed for {Tenant}: {Error}", tenant.Name, result.Error);
                continue;
            }

            foreach (var reply in result.Value!)
            {
                // Match by MessageId first, then subject fallback
                var email = await outreachRepo.GetEmailByMessageIdAsync(tenant.Id, reply.MessageId, ct);
                if (email is null)
                {
                    var normalizedSubject = reply.Subject.ToLowerInvariant().Replace("re: ", "");
                    // Subject fallback would need contact matching — log for manual review
                    logger.LogWarning("Unmatched reply from {From}: {Subject}", reply.FromAddress, reply.Subject);
                    continue;
                }

                email.RecordReply(reply.ReceivedAt);

                var enrollment = await outreachRepo.GetEnrollmentByIdAsync(email.EnrollmentId, ct);
                enrollment?.MarkReplied();

                await auditLog.AppendAsync(Domain.Audit.AuditEvent.Record(
                    tenant.Id, "EmailActivity", email.Id, "ReplyDetected", "system",
                    System.Text.Json.JsonSerializer.Serialize(new { reply.FromAddress, reply.Subject, reply.ReceivedAt })), ct);

                logger.LogInformation("Reply detected from {From} for enrollment {EnrollmentId}",
                    reply.FromAddress, email.EnrollmentId);
            }

            await outreachRepo.SaveChangesAsync(ct);
        }
    }
}
