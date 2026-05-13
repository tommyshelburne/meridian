namespace Meridian.Application.Outreach;

public record ReplyListItem(
    Guid EmailActivityId,
    Guid OpportunityId,
    string OpportunityTitle,
    Guid ContactId,
    string ContactName,
    string? ContactEmail,
    string Subject,
    int StepNumber,
    DateTimeOffset RepliedAt,
    string? ReplyBody,
    string? SuppressionReason);
