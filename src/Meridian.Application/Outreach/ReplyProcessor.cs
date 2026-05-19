using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Outreach;

public record DetectedReply(string MessageId, string Subject, DateTimeOffset ReceivedAt, string FromAddress)
{
    public string? Body { get; init; }
    public bool IsAutoReply { get; init; }
}

public class ReplyProcessor
{
    private static readonly Regex SubjectPrefixPattern =
        new(@"^\s*(?:re|fw|fwd|aw)\s*(?:\[\d+\])?\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IOutreachRepository _outreachRepo;
    private readonly IContactRepository _contactRepo;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<ReplyProcessor> _logger;

    public ReplyProcessor(
        IOutreachRepository outreachRepo,
        IContactRepository contactRepo,
        IAuditLog auditLog,
        ILogger<ReplyProcessor> logger)
    {
        _outreachRepo = outreachRepo;
        _contactRepo = contactRepo;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<ServiceResult<ReplyProcessSummary>> ProcessAsync(
        Guid tenantId, IReadOnlyList<DetectedReply> replies, CancellationToken ct)
    {
        var summary = new ReplyProcessSummary();

        foreach (var reply in replies)
        {
            var matched = await MatchReplyAsync(tenantId, reply, ct);
            if (matched is null)
            {
                summary.Unmatched++;
                _logger.LogWarning("Unmatched reply from {From}: {Subject}", reply.FromAddress, reply.Subject);
                continue;
            }

            if (reply.IsAutoReply)
            {
                summary.AutoReplies++;
                _logger.LogInformation(
                    "Auto-reply from {From} suppressed; enrollment {EnrollmentId} not halted.",
                    reply.FromAddress, matched.Activity.EnrollmentId);

                matched.Activity.RecordSuppressedReply(reply.ReceivedAt, reply.Body, "out_of_office");

                await _auditLog.AppendAsync(AuditEvent.Record(
                    tenantId, "EmailActivity", matched.Activity.Id, "AutoReplyDetected", "system",
                    JsonSerializer.Serialize(new
                    {
                        reply.FromAddress,
                        reply.Subject,
                        reply.ReceivedAt,
                        matched.MatchStrategy
                    })), ct);
                continue;
            }

            matched.Activity.RecordReply(reply.ReceivedAt, reply.Body);

            var enrollment = await _outreachRepo.GetEnrollmentByIdAsync(matched.Activity.EnrollmentId, ct);
            enrollment?.MarkReplied();

            await _auditLog.AppendAsync(AuditEvent.Record(
                tenantId, "EmailActivity", matched.Activity.Id, "ReplyDetected", "system",
                JsonSerializer.Serialize(new
                {
                    reply.FromAddress,
                    reply.Subject,
                    reply.ReceivedAt,
                    matched.MatchStrategy,
                    bodyLength = reply.Body?.Length ?? 0
                })), ct);

            if (matched.MatchStrategy == "messageId") summary.MatchedByMessageId++;
            else summary.MatchedBySubject++;
        }

        await _outreachRepo.SaveChangesAsync(ct);
        return ServiceResult<ReplyProcessSummary>.Ok(summary);
    }

    private async Task<MatchedReply?> MatchReplyAsync(Guid tenantId, DetectedReply reply, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(reply.MessageId))
        {
            var byMessageId = await _outreachRepo.GetEmailByMessageIdAsync(tenantId, reply.MessageId, ct);
            if (byMessageId is not null)
                return new MatchedReply(byMessageId, "messageId");
        }

        if (string.IsNullOrWhiteSpace(reply.FromAddress) || string.IsNullOrWhiteSpace(reply.Subject))
            return null;

        var contact = await _contactRepo.GetByEmailAsync(tenantId, reply.FromAddress, ct);
        if (contact is null) return null;

        var normalizedSubject = NormalizeSubject(reply.Subject);
        if (string.IsNullOrEmpty(normalizedSubject)) return null;

        var bySubject = await _outreachRepo.GetEmailBySubjectAndContactAsync(
            tenantId, normalizedSubject, contact.Id, ct);
        return bySubject is null ? null : new MatchedReply(bySubject, "subject");
    }

    public static string NormalizeSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return string.Empty;

        var current = subject.Trim();
        while (true)
        {
            var stripped = SubjectPrefixPattern.Replace(current, string.Empty);
            if (stripped == current) break;
            current = stripped.Trim();
        }
        return current;
    }

    private record MatchedReply(Domain.Outreach.EmailActivity Activity, string MatchStrategy);
}

public record ReplyProcessSummary
{
    public int MatchedByMessageId { get; set; }
    public int MatchedBySubject { get; set; }
    public int AutoReplies { get; set; }
    public int Unmatched { get; set; }
    public int Total => MatchedByMessageId + MatchedBySubject + AutoReplies + Unmatched;
}
