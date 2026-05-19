using Meridian.Domain.Audit;
using Meridian.Domain.Auth;
using Meridian.Domain.Contacts;
using Meridian.Domain.Crm;
using Meridian.Domain.Markets;
using Meridian.Domain.Memory;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Outreach;
using Meridian.Domain.Sources;
using Meridian.Domain.Tenants;
using Meridian.Domain.Users;
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
    public DbSet<MarketProfile> MarketProfiles => Set<MarketProfile>();
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
    public DbSet<User> Users => Set<User>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<OidcConfig> OidcConfigs => Set<OidcConfig>();
    public DbSet<SourceDefinition> SourceDefinitions => Set<SourceDefinition>();
    public DbSet<OutboundConfiguration> OutboundConfigurations => Set<OutboundConfiguration>();
    public DbSet<CrmConnection> CrmConnections => Set<CrmConnection>();
    public DbSet<WebhookPayloadRecord> WebhookPayloads => Set<WebhookPayloadRecord>();

    // Global market reference data — shared across all tenants, NOT tenant-owned.
    public DbSet<ProcurementMarketCell> ProcurementMarketCells => Set<ProcurementMarketCell>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MeridianDbContext).Assembly);

        // Global query filters for multi-tenancy — reference the context field so EF
        // parameterizes the current TenantId at query time rather than baking in the
        // value from when the model was first built.
        modelBuilder.Entity<MarketProfile>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<OidcConfig>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Opportunity>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Contact>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<OutreachSequence>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<OutreachEnrollment>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<EmailActivity>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<OutreachTemplate>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<AuditEvent>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<RagMemory>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<SuppressionEntry>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<SourceDefinition>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<OutboundConfiguration>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<CrmConnection>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);

        // ProcurementMarketCell is GLOBAL market reference data shared across all
        // tenants. It deliberately receives NO HasQueryFilter — adding a tenant
        // filter here would be a bug, since the entity has no TenantId and the
        // data is not tenant-owned.
    }
}
