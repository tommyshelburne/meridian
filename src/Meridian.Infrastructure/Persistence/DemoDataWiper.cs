using Meridian.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence;

// Removes every tenant-owned row for a demo tenant via the change tracker.
// Demo tenants are tiny by construction (a few dozen rows), so loading and
// RemoveRange-ing keeps this portable across Postgres and the SQLite test
// fixture, and sidesteps ExecuteDelete's owned-type and cascade caveats.
// Queries pin TenantId explicitly (plus IgnoreQueryFilters) so the outcome
// never depends on ambient tenant context. The Tenant row, Users,
// memberships, and auth tokens deliberately survive.
public class DemoDataWiper : IDemoDataWiper
{
    private readonly MeridianDbContext _db;

    public DemoDataWiper(MeridianDbContext db)
    {
        _db = db;
    }

    public async Task<int> WipeTenantDataAsync(Guid tenantId, CancellationToken ct)
    {
        var sequenceIds = await _db.OutreachSequences.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId).Select(s => s.Id).ToListAsync(ct);
        var opportunityIds = await _db.Opportunities.IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId).Select(o => o.Id).ToListAsync(ct);

        // Children first, then their parents, so FK constraints hold at save.
        _db.EmailActivities.RemoveRange(await _db.EmailActivities.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId).ToListAsync(ct));
        _db.OutreachEnrollments.RemoveRange(await _db.OutreachEnrollments.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId).ToListAsync(ct));
        _db.SequenceSnapshots.RemoveRange(await _db.SequenceSnapshots
            .Where(s => sequenceIds.Contains(s.SequenceId)).ToListAsync(ct));
        _db.SequenceSteps.RemoveRange(await _db.SequenceSteps
            .Where(s => sequenceIds.Contains(s.SequenceId)).ToListAsync(ct));
        _db.OutreachSequences.RemoveRange(await _db.OutreachSequences.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId).ToListAsync(ct));
        _db.OutreachTemplates.RemoveRange(await _db.OutreachTemplates.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId).ToListAsync(ct));
        _db.OpportunityContacts.RemoveRange(await _db.OpportunityContacts
            .Where(oc => opportunityIds.Contains(oc.OpportunityId)).ToListAsync(ct));
        _db.Contacts.RemoveRange(await _db.Contacts.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId).ToListAsync(ct));
        _db.Opportunities.RemoveRange(await _db.Opportunities.IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId).ToListAsync(ct));
        _db.SuppressionEntries.RemoveRange(await _db.SuppressionEntries.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId).ToListAsync(ct));
        _db.AuditEvents.RemoveRange(await _db.AuditEvents.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId).ToListAsync(ct));
        _db.RagMemories.RemoveRange(await _db.RagMemories.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId).ToListAsync(ct));
        _db.SourceDefinitions.RemoveRange(await _db.SourceDefinitions.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId).ToListAsync(ct));
        _db.WebhookPayloads.RemoveRange(await _db.WebhookPayloads
            .Where(w => w.TenantId == tenantId).ToListAsync(ct));
        _db.OutboundConfigurations.RemoveRange(await _db.OutboundConfigurations.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId).ToListAsync(ct));
        _db.CrmConnections.RemoveRange(await _db.CrmConnections.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId).ToListAsync(ct));
        _db.MarketProfiles.RemoveRange(await _db.MarketProfiles.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId).ToListAsync(ct));

        return await _db.SaveChangesAsync(ct);
    }
}
