using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;

namespace Meridian.Application.Opportunities;

public record OpportunityDetail(
    Opportunity Opportunity,
    IReadOnlyList<Contact> Contacts,
    IReadOnlyList<AuditEvent> RecentEvents);

public class OpportunityDetailService
{
    private readonly IOpportunityRepository _opportunities;
    private readonly IContactRepository _contacts;
    private readonly IAuditLog _auditLog;

    public OpportunityDetailService(
        IOpportunityRepository opportunities,
        IContactRepository contacts,
        IAuditLog auditLog)
    {
        _opportunities = opportunities;
        _contacts = contacts;
        _auditLog = auditLog;
    }

    public async Task<OpportunityDetail?> GetAsync(Guid tenantId, Guid opportunityId, CancellationToken ct)
    {
        var opp = await _opportunities.GetByIdAsync(opportunityId, ct);
        if (opp is null || opp.TenantId != tenantId) return null;

        var contacts = new List<Contact>();
        foreach (var link in opp.Contacts)
        {
            var contact = await _contacts.GetByIdAsync(link.ContactId, ct);
            if (contact is not null) contacts.Add(contact);
        }

        var events = await _auditLog.QueryAsync(
            tenantId,
            entityType: "Opportunity",
            eventType: null,
            from: null, to: null,
            limit: 20, ct);

        var forThisOpp = events.Where(e => e.EntityId == opp.Id).ToList();

        return new OpportunityDetail(opp, contacts, forThisOpp);
    }
}
