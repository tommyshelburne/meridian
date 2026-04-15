using Meridian.Domain.Audit;
using Meridian.Domain.Contacts;
using Meridian.Domain.Memory;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Outreach;
using Meridian.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence;

public class MeridianDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public MeridianDbContext(DbContextOptions<MeridianDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<OutreachSequence> OutreachSequences => Set<OutreachSequence>();
    public DbSet<SequenceStep> SequenceSteps => Set<SequenceStep>();
    public DbSet<OutreachEnrollment> OutreachEnrollments => Set<OutreachEnrollment>();
    public DbSet<EmailActivity> EmailActivities => Set<EmailActivity>();
    public DbSet<OutreachTemplate> OutreachTemplates => Set<OutreachTemplate>();
    public DbSet<SequenceSnapshot> SequenceSnapshots => Set<SequenceSnapshot>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<RagMemory> RagMemories => Set<RagMemory>();
    public DbSet<SuppressionEntry> SuppressionEntries => Set<SuppressionEntry>();
    public DbSet<OpportunityContact> OpportunityContacts => Set<OpportunityContact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MeridianDbContext).Assembly);

        // Global query filters for multi-tenancy
        var tenantId = _tenantContext.TenantId;

        modelBuilder.Entity<Opportunity>().HasQueryFilter(e => e.TenantId == tenantId);
        modelBuilder.Entity<Contact>().HasQueryFilter(e => e.TenantId == tenantId);
        modelBuilder.Entity<OutreachSequence>().HasQueryFilter(e => e.TenantId == tenantId);
        modelBuilder.Entity<OutreachEnrollment>().HasQueryFilter(e => e.TenantId == tenantId);
        modelBuilder.Entity<EmailActivity>().HasQueryFilter(e => e.TenantId == tenantId);
        modelBuilder.Entity<OutreachTemplate>().HasQueryFilter(e => e.TenantId == tenantId);
        modelBuilder.Entity<AuditEvent>().HasQueryFilter(e => e.TenantId == tenantId);
        modelBuilder.Entity<RagMemory>().HasQueryFilter(e => e.TenantId == tenantId);
        modelBuilder.Entity<SuppressionEntry>().HasQueryFilter(e => e.TenantId == tenantId);
    }
}
