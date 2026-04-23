using System.Text.Json;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Meridian.Domain.Outreach;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Outreach;

public enum BounceEventKind
{
    HardBounce,
    SoftBounce,
    SpamComplaint
}

public record BounceEvent(
    string Email,
    BounceEventKind Kind,
    string? ProviderReason,
    DateTimeOffset OccurredAt);

public class BounceProcessor
{
    private readonly IContactRepository _contacts;
    private readonly IOutreachRepository _outreach;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<BounceProcessor> _logger;

    public BounceProcessor(
        IContactRepository contacts,
        IOutreachRepository outreach,
        IAuditLog auditLog,
        ILogger<BounceProcessor> logger)
    {
        _contacts = contacts;
        _outreach = outreach;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<ServiceResult<BounceProcessSummary>> ProcessAsync(
        Guid tenantId, IReadOnlyList<BounceEvent> events, CancellationToken ct)
    {
        var summary = new BounceProcessSummary();

        foreach (var evt in events)
        {
            if (string.IsNullOrWhiteSpace(evt.Email))
            {
                summary.Skipped++;
                continue;
            }

            // Soft bounces increment a counter on the contact; on the third one
            // the contact is escalated to permanently bounced (spec section 5.1)
            // and we suppress + stop enrollments just like a hard bounce.
            if (evt.Kind == BounceEventKind.SoftBounce)
            {
                var softContact = await _contacts.GetByEmailAsync(tenantId, evt.Email, ct);
                var escalated = softContact?.RecordSoftBounce() ?? false;

                if (escalated)
                {
                    await SuppressAsync(tenantId, evt.Email, BuildSuppressionReason(evt), ct);
                    var enrollments = await _outreach.GetActiveEnrollmentsForContactAsync(tenantId, softContact!.Id, ct);
                    foreach (var enrollment in enrollments)
                    {
                        enrollment.MarkBounced();
                        summary.EnrollmentsStopped++;
                    }
                    summary.HardBounces++;
                    await AppendAuditAsync(tenantId, evt, "SoftBounceEscalated", ct);
                }
                else
                {
                    await AppendAuditAsync(tenantId, evt, "SoftBounce", ct);
                    summary.SoftBounces++;
                }
                continue;
            }

            await SuppressAsync(tenantId, evt.Email, BuildSuppressionReason(evt), ct);

            var contact = await _contacts.GetByEmailAsync(tenantId, evt.Email, ct);
            if (contact is not null)
            {
                contact.MarkBounced();

                var enrollments = await _outreach.GetActiveEnrollmentsForContactAsync(tenantId, contact.Id, ct);
                foreach (var enrollment in enrollments)
                {
                    enrollment.MarkBounced();
                    summary.EnrollmentsStopped++;
                }
            }
            else
            {
                _logger.LogInformation(
                    "Bounce event for unknown contact {Email}; suppression added but no enrollment to stop",
                    evt.Email);
            }

            await AppendAuditAsync(tenantId, evt, evt.Kind == BounceEventKind.SpamComplaint ? "SpamComplaint" : "HardBounce", ct);
            if (evt.Kind == BounceEventKind.HardBounce) summary.HardBounces++;
            else summary.SpamComplaints++;
        }

        await _outreach.SaveChangesAsync(ct);
        await _contacts.SaveChangesAsync(ct);

        return ServiceResult<BounceProcessSummary>.Ok(summary);
    }

    private async Task SuppressAsync(Guid tenantId, string email, string reason, CancellationToken ct)
    {
        if (await _outreach.IsSuppressedAsync(tenantId, email, ct)) return;
        var entry = SuppressionEntry.Create(tenantId, email, SuppressionType.Email, reason);
        await _outreach.AddSuppressionAsync(entry, ct);
    }

    private Task AppendAuditAsync(Guid tenantId, BounceEvent evt, string eventType, CancellationToken ct)
        => _auditLog.AppendAsync(AuditEvent.Record(
            tenantId, "Contact", Guid.Empty, eventType, "system",
            JsonSerializer.Serialize(new { evt.Email, evt.ProviderReason, evt.OccurredAt })), ct);

    private static string BuildSuppressionReason(BounceEvent evt) => evt.Kind switch
    {
        BounceEventKind.HardBounce => $"hard_bounce: {evt.ProviderReason ?? "unspecified"}",
        BounceEventKind.SpamComplaint => $"spam_complaint: {evt.ProviderReason ?? "unspecified"}",
        _ => "unknown"
    };
}

public record BounceProcessSummary
{
    public int HardBounces { get; set; }
    public int SoftBounces { get; set; }
    public int SpamComplaints { get; set; }
    public int EnrollmentsStopped { get; set; }
    public int Skipped { get; set; }
    public int Total => HardBounces + SoftBounces + SpamComplaints + Skipped;
}
