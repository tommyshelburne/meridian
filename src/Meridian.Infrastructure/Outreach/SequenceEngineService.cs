using System.Text.Json;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Meridian.Domain.Outreach;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Outreach;

public class SequenceEngineService : ISequenceEngine
{
    private readonly IOutreachRepository _outreachRepo;
    private readonly IContactRepository _contactRepo;
    private readonly IOpportunityRepository _opportunityRepo;
    private readonly IEmailSender _emailSender;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly IAuditLog _auditLog;
    private readonly SendThrottleState _throttle;
    private readonly ILogger<SequenceEngineService> _logger;

    public SequenceEngineService(
        IOutreachRepository outreachRepo,
        IContactRepository contactRepo,
        IOpportunityRepository opportunityRepo,
        IEmailSender emailSender,
        ITemplateRenderer templateRenderer,
        IAuditLog auditLog,
        SendThrottleState throttle,
        ILogger<SequenceEngineService> logger)
    {
        _outreachRepo = outreachRepo;
        _contactRepo = contactRepo;
        _opportunityRepo = opportunityRepo;
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _auditLog = auditLog;
        _throttle = throttle;
        _logger = logger;
    }

    public async Task<ServiceResult<int>> ProcessDueEnrollmentsAsync(Guid tenantId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var dueEnrollments = await _outreachRepo.GetDueEnrollmentsAsync(tenantId, now, ct);
        var sent = 0;

        foreach (var enrollment in dueEnrollments)
        {
            if (_throttle.IsCapReached)
            {
                _logger.LogWarning("Daily send cap reached. {Remaining} enrollments deferred to next day",
                    dueEnrollments.Count - sent);
                break;
            }

            try
            {
                var snapshot = await _outreachRepo.GetSnapshotByIdAsync(enrollment.SequenceSnapshotId, ct);
                if (snapshot is null)
                {
                    _logger.LogWarning("Snapshot {SnapshotId} not found for enrollment {EnrollmentId}",
                        enrollment.SequenceSnapshotId, enrollment.Id);
                    continue;
                }

                var steps = JsonSerializer.Deserialize<List<SnapshotStep>>(snapshot.SnapshotJson);
                var currentStep = steps?.FirstOrDefault(s => s.StepNumber == enrollment.CurrentStep);
                if (currentStep is null)
                {
                    _logger.LogWarning("Step {Step} not found in snapshot for enrollment {EnrollmentId}",
                        enrollment.CurrentStep, enrollment.Id);
                    continue;
                }

                // Check send window
                if (!IsWithinSendWindow(currentStep, now))
                    continue;

                var contact = await _contactRepo.GetByIdAsync(enrollment.ContactId, ct);
                var opportunity = await _opportunityRepo.GetByIdAsync(enrollment.OpportunityId, ct);
                if (contact is null || opportunity is null) continue;

                // Check suppression
                if (contact.IsOptedOut || contact.IsBounced || contact.Email is null)
                {
                    enrollment.MarkUnsubscribed();
                    continue;
                }

                // Render template
                var tokens = BuildTokens(contact, opportunity, enrollment);
                var subjectResult = _templateRenderer.Render(currentStep.Subject, tokens);
                var bodyResult = _templateRenderer.Render(currentStep.BodyTemplate, tokens);
                if (!subjectResult.IsSuccess || !bodyResult.IsSuccess)
                {
                    _logger.LogWarning("Template render failed for enrollment {EnrollmentId}: {Error}",
                        enrollment.Id, subjectResult.Error ?? bodyResult.Error);
                    continue;
                }

                // Apply jitter
                var jitterMs = currentStep.JitterMinutes > 0
                    ? Random.Shared.Next(0, currentStep.JitterMinutes * 60 * 1000)
                    : 0;
                if (jitterMs > 0)
                    await Task.Delay(jitterMs, ct);

                // Send — sender identity will be sourced from tenant outbound settings in a later phase
                var message = new EmailMessage(
                    contact.Email,
                    "outreach@meridian.local",
                    "Meridian",
                    subjectResult.Value!,
                    bodyResult.Value!);

                var sendResult = await _emailSender.SendAsync(message, ct);
                if (!sendResult.IsSuccess)
                {
                    _logger.LogWarning("Email send failed for enrollment {EnrollmentId}: {Error}",
                        enrollment.Id, sendResult.Error);
                    continue;
                }

                // Record activity
                var activity = EmailActivity.Record(
                    tenantId, enrollment.Id, contact.Id, opportunity.Id,
                    enrollment.CurrentStep, subjectResult.Value!, bodyResult.Value!,
                    sendResult.Value!.MessageId);
                await _outreachRepo.AddEmailActivityAsync(activity, ct);

                // Advance enrollment
                var totalSteps = steps!.Count;
                if (enrollment.CurrentStep < totalSteps)
                {
                    var nextStep = steps.First(s => s.StepNumber == enrollment.CurrentStep + 1);
                    var nextSend = now.AddDays(nextStep.DelayDays);
                    enrollment.AdvanceStep(nextSend, totalSteps);
                }
                else
                {
                    enrollment.AdvanceStep(now, totalSteps);
                }

                _throttle.RecordSend();
                sent++;

                await _auditLog.AppendAsync(AuditEvent.Record(
                    tenantId, "OutreachEnrollment", enrollment.Id, "EmailSent", "system",
                    JsonSerializer.Serialize(new { contact.Email, Step = enrollment.CurrentStep - 1, opportunity.Title })), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing enrollment {EnrollmentId}", enrollment.Id);
            }
        }

        await _outreachRepo.SaveChangesAsync(ct);
        return ServiceResult<int>.Ok(sent);
    }

    private static bool IsWithinSendWindow(SnapshotStep step, DateTimeOffset now)
    {
        var timeOfDay = now.TimeOfDay;
        return timeOfDay >= step.SendWindowStart && timeOfDay <= step.SendWindowEnd;
    }

    private static IDictionary<string, object> BuildTokens(
        Domain.Contacts.Contact contact,
        Domain.Opportunities.Opportunity opportunity,
        OutreachEnrollment enrollment)
    {
        return new Dictionary<string, object>
        {
            ["contact"] = new Dictionary<string, object>
            {
                ["name"] = contact.FullName,
                ["first_name"] = contact.FullName.Split(' ').FirstOrDefault() ?? contact.FullName,
                ["title"] = contact.Title ?? "",
                ["email"] = contact.Email ?? ""
            },
            ["agency"] = new Dictionary<string, object>
            {
                ["name"] = opportunity.Agency.Name
            },
            ["opportunity"] = new Dictionary<string, object>
            {
                ["title"] = opportunity.Title,
                ["deadline"] = opportunity.ResponseDeadline?.ToString("MMMM d, yyyy") ?? "N/A"
            },
            ["sequence"] = new Dictionary<string, object>
            {
                ["step"] = enrollment.CurrentStep
            }
        };
    }
}

internal record SnapshotStep(
    int StepNumber,
    int DelayDays,
    string Subject,
    string BodyTemplate,
    TimeSpan SendWindowStart,
    TimeSpan SendWindowEnd,
    int JitterMinutes);
